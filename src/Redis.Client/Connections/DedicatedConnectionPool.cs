using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;

namespace Redis.Client.Connections;

/// <summary>
/// A bounded pool of request/response connections for operations that block a connection or depend on its state:
/// blocking commands and WATCH transactions. Each pooled connection has completed the same handshake as the
/// multiplexed ones.
/// </summary>
internal sealed class DedicatedConnectionPool : IAsyncDisposable
{
    private readonly RedisClientOptions _options;
    private readonly ILogger _logger;
    private readonly ClientInstrumentation _instrumentation;
    private readonly SemaphoreSlim _slots;
    private readonly Stack<PhysicalConnection> _idle = new();
    private int _disposed;

    public DedicatedConnectionPool(RedisClientOptions options, ILogger logger, ClientInstrumentation instrumentation)
    {
        _options = options;
        _logger = logger;
        _instrumentation = instrumentation;
        _slots = new SemaphoreSlim(options.DedicatedPoolSize, options.DedicatedPoolSize);
    }

    /// <summary>Connections currently leased.</summary>
    public int InUse => _options.DedicatedPoolSize - _slots.CurrentCount;

    /// <summary>Leases a connection, waiting at most <see cref="RedisClientOptions.CommandTimeout"/> for a free slot.</summary>
    public async ValueTask<DedicatedConnectionLease> LeaseAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_slots.Wait(0))
        {
            Log.DedicatedPoolExhausted(_logger, RedisClientOptions.FormatEndpoint(_options.Endpoints[0]), _options.DedicatedPoolSize);
            var waited = Stopwatch.StartNew();
            var acquired = await _slots.WaitAsync(_options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            _instrumentation.DedicatedPoolWait(waited.Elapsed);
            if (!acquired)
            {
                throw new RedisTimeoutException(
                    $"All {_options.DedicatedPoolSize} dedicated connections stayed busy for {_options.CommandTimeout}; the command was not sent.",
                    mayHaveExecuted: false);
            }
        }

        try
        {
            var connection = TakeIdle();
            if (connection is null)
            {
                connection = await PhysicalConnection.ConnectAsync(_options, ConnectionKind.Dedicated, _logger, cancellationToken).ConfigureAwait(false);
                _instrumentation.ConnectionOpened(ConnectionKind.Dedicated);
            }

            return new DedicatedConnectionLease(this, connection);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        PhysicalConnection[] idle;
        lock (_idle)
        {
            idle = [.. _idle];
            _idle.Clear();
        }

        foreach (var connection in idle)
        {
            _instrumentation.ConnectionClosed(ConnectionKind.Dedicated);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal async ValueTask ReturnAsync(PhysicalConnection connection, bool reusable)
    {
        try
        {
            if (reusable && Volatile.Read(ref _disposed) == 0 && connection.IsAlive())
            {
                lock (_idle)
                {
                    _idle.Push(connection);
                }

                return;
            }

            _instrumentation.ConnectionClosed(ConnectionKind.Dedicated);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _slots.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private PhysicalConnection? TakeIdle()
    {
        while (true)
        {
            PhysicalConnection? connection;
            lock (_idle)
            {
                if (!_idle.TryPop(out connection))
                {
                    return null;
                }
            }

            if (connection.IsAlive())
            {
                return connection;
            }

            _instrumentation.ConnectionClosed(ConnectionKind.Dedicated);
            _ = connection.DisposeAsync();
        }
    }
}

/// <summary>
/// Exclusive use of a dedicated connection. Commands run one at a time. If a command times out or is cancelled
/// after being sent, the connection state is unknown and it is closed instead of being returned to the pool.
/// </summary>
internal sealed class DedicatedConnectionLease : IAsyncDisposable
{
    private readonly DedicatedConnectionPool _pool;
    private bool _broken;
    private int _disposed;

    public DedicatedConnectionLease(DedicatedConnectionPool pool, PhysicalConnection connection)
    {
        _pool = pool;
        Connection = connection;
    }

    public PhysicalConnection Connection { get; }

    /// <summary>Marks the connection as unusable (e.g. its transaction state could not be reset).</summary>
    public void MarkBroken() => _broken = true;

    /// <summary>Sends one command and waits for its reply for at most <paramref name="timeout"/>.</summary>
    public async Task<T> ExecuteAsync<T>(RedisCommand command, Func<RespFrame, T> processor, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_broken)
        {
            throw new RedisConnectionException("The dedicated connection is no longer usable after a previous failure.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            deadline.CancelAfter(timeout);
        }

        var written = false;
        Exception? replyError = null;
        T result;
        try
        {
            Connection.Write(command);
            written = true;
            await Connection.FlushAsync(deadline.Token).ConfigureAwait(false);

            // Errors in the reply (server errors, conversion failures) leave the connection in sync; keep them apart
            // from transport failures, which make it unusable.
            result = await Connection.ReadReplyAsync(
                frame =>
                {
                    if (frame.IsError)
                    {
                        replyError = frame.ToServerException();
                        return default!;
                    }

                    try
                    {
                        return processor(frame);
                    }
                    catch (Exception ex)
                    {
                        replyError = ex;
                        return default!;
                    }
                },
                deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _broken = true;
            throw;
        }
        catch (OperationCanceledException)
        {
            _broken = true;
            throw new RedisTimeoutException($"No reply to {command} within {timeout}. The command was sent and may have been executed.", mayHaveExecuted: written);
        }
        catch (Exception ex)
        {
            _broken = true;
            throw new RedisConnectionException(
                $"The dedicated connection to {Connection.EndpointText} failed during {command}; it may have been executed.", ex, mayHaveExecuted: written);
        }

        if (replyError is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(replyError);
        }

        return result;
    }

    /// <summary>Sends a transaction block and feeds every reply to it until it is complete.</summary>
    public async Task<bool> ExecuteOperationAsync(Transactions.PendingTransaction operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (_broken)
        {
            throw new RedisConnectionException("The dedicated connection is no longer usable after a previous failure.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(operation.Timeout);
        try
        {
            operation.TryMarkWritten();
            operation.Write(Connection);
            await Connection.FlushAsync(deadline.Token).ConfigureAwait(false);
            var complete = false;
            while (!complete)
            {
                complete = await Connection.ReadReplyAsync(operation.OnFrame, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _broken = true;
            operation.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (OperationCanceledException)
        {
            _broken = true;
            var timeout = new RedisTimeoutException($"No reply to EXEC within {operation.Timeout}. The transaction was sent and may have been executed.", mayHaveExecuted: true);
            operation.TrySetException(timeout);
            throw timeout;
        }
        catch (Exception ex)
        {
            _broken = true;
            var failure = new RedisConnectionException(
                $"The dedicated connection to {Connection.EndpointText} failed during EXEC; the transaction may have been executed.", ex, mayHaveExecuted: true);
            operation.TrySetException(failure);
            throw failure;
        }

        return await operation.Task.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _disposed, 1) != 0 ? ValueTask.CompletedTask : _pool.ReturnAsync(Connection, !_broken);
}
