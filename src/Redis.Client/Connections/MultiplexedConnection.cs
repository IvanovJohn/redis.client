using Microsoft.Extensions.Logging;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;

namespace Redis.Client.Connections;

internal enum ConnectionState
{
    NotStarted,
    Connecting,
    Connected,
    Reconnecting,
    Disposed,
}

/// <summary>
/// A long-lived logical connection shared by all callers. Commands are queued, written in batches by a single
/// writer (pipelining) and matched to replies in order by a single reader. The physical connection underneath is
/// replaced transparently after failures; queued commands survive reconnects according to
/// <see cref="RedisClientOptions.BacklogPolicy"/>.
/// </summary>
internal sealed class MultiplexedConnection : IAsyncDisposable
{
    private const int MaxBatchSize = 1024;

    private readonly RedisClientOptions _options;
    private readonly ILogger _logger;
    private readonly ClientInstrumentation _instrumentation;
    private readonly object _writeLock = new();
    private readonly Queue<PendingOperation> _writeQueue = new();
    private readonly SemaphoreSlim _writeSignal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private int _writeSignaled;
    private int _started;
    private int _disposed;
    private int _state = (int)ConnectionState.NotStarted;
    private bool _everConnected;
    private Session? _session;
    private Exception? _lastError;
    private TaskCompletionSource _connectOutcome = NewOutcome();
    private Task _connectionLoop = Task.CompletedTask;
    private Task _heartbeatLoop = Task.CompletedTask;

    public MultiplexedConnection(RedisClientOptions options, ILogger logger, ClientInstrumentation instrumentation)
    {
        _options = options;
        _logger = logger;
        _instrumentation = instrumentation;
    }

    public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

    /// <summary>Commands waiting to be written.</summary>
    public int BacklogCount
    {
        get
        {
            lock (_writeLock)
            {
                return _writeQueue.Count;
            }
        }
    }

    /// <summary>Commands written and waiting for a reply.</summary>
    public int PendingCount => Volatile.Read(ref _session)?.PendingCount ?? 0;

    /// <summary>The current physical connection, if connected (for diagnostics and tests).</summary>
    public PhysicalConnection? Connection => Volatile.Read(ref _session)?.Connection;

    /// <summary>Receives RESP3 push frames that arrive on this connection.</summary>
    public Action<RespFrame>? PushHandler { get; set; }

    /// <summary>Starts connecting (if not started) and waits for the first successful connection or failure.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        EnsureStarted();
        if (Volatile.Read(ref _session) is not null)
        {
            return;
        }

        var outcome = Volatile.Read(ref _connectOutcome);
        if (Volatile.Read(ref _session) is not null)
        {
            return;
        }

        await outcome.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Queues an operation for writing. Never blocks.</summary>
    public void Enqueue(PendingOperation operation)
    {
        EnsureStarted();
        lock (_writeLock)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                operation.TryFailUnwritten(new ObjectDisposedException("RedisClient", "The Redis client has been disposed."));
                return;
            }

            if (_options.BacklogPolicy == BacklogPolicy.FailFast && _everConnected && Volatile.Read(ref _session) is null)
            {
                operation.TryFailUnwritten(new RedisConnectionException(
                    $"The connection to {Endpoint} is down and {nameof(BacklogPolicy)} is {BacklogPolicy.FailFast}.", _lastError, mayHaveExecuted: false));
                return;
            }

            _writeQueue.Enqueue(operation);
        }

        SignalWriter();
    }

    /// <summary>Forces the current physical connection to fail (used by keep-alive and tests).</summary>
    public void Abort(Exception reason) => Volatile.Read(ref _session)?.Fail(reason);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _state, (int)ConnectionState.Disposed);
        List<PendingOperation> unwritten;
        lock (_writeLock)
        {
            unwritten = [.. _writeQueue];
            _writeQueue.Clear();
        }

        var disposed = new ObjectDisposedException("RedisClient", "The Redis client was disposed before the command was sent.");
        foreach (var operation in unwritten)
        {
            operation.TryFailUnwritten(disposed);
        }

        // Give commands already on the wire a chance to receive their replies.
        var session = Volatile.Read(ref _session);
        if (session is not null)
        {
            await session.WaitForPendingAsync(_options.CommandTimeout).ConfigureAwait(false);
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        session?.Fail(new ObjectDisposedException("RedisClient", "The Redis client has been disposed."));
        await Task.WhenAll(_connectionLoop, _heartbeatLoop).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Volatile.Read(ref _connectOutcome).TrySetException(new ObjectDisposedException("RedisClient"));
        _shutdown.Dispose();
        _writeSignal.Dispose();
    }

    private string Endpoint => RedisClientOptions.FormatEndpoint(_options.Endpoints[0]);

    private static TaskCompletionSource NewOutcome() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref _state, (int)ConnectionState.Connecting);
        _connectionLoop = Task.Run(ConnectionLoopAsync);
        _heartbeatLoop = Task.Run(HeartbeatLoopAsync);
    }

    private void SignalWriter()
    {
        if (Interlocked.Exchange(ref _writeSignaled, 1) == 0)
        {
            try
            {
                _writeSignal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void SignalConnectOutcome(Exception? error)
    {
        var outcome = Interlocked.Exchange(ref _connectOutcome, NewOutcome());
        if (error is null)
        {
            outcome.TrySetResult();
        }
        else
        {
            outcome.TrySetException(error);
        }
    }

    private async Task ConnectionLoopAsync()
    {
        var token = _shutdown.Token;
        var attempt = 0;
        while (!token.IsCancellationRequested)
        {
            PhysicalConnection connection;
            try
            {
                connection = await PhysicalConnection.ConnectAsync(_options, ConnectionKind.Multiplexed, _logger, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                _lastError = ex;
                SignalConnectOutcome(ex);
                FailBacklogAfterConnectError(ex);
                var delay = Backoff.GetDelay(attempt, _options.ReconnectBaseDelay, _options.ReconnectMaxDelay);
                Log.ConnectFailed(_logger, Endpoint, attempt, delay, ex);
                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            if (_everConnected)
            {
                _instrumentation.Reconnected();
                Log.Reconnected(_logger, nameof(ConnectionKind.Multiplexed), Endpoint, attempt + 1);
            }

            attempt = 0;
            var session = new Session(this, connection);
            lock (_writeLock)
            {
                _everConnected = true;
                Volatile.Write(ref _session, session);
            }

            Volatile.Write(ref _state, (int)ConnectionState.Connected);
            _instrumentation.ConnectionOpened(ConnectionKind.Multiplexed);
            SignalConnectOutcome(null);

            Exception error;
            try
            {
                error = await session.RunAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Never let a failure while tearing down a session end the reconnect loop.
                error = ex;
            }

            _instrumentation.ConnectionClosed(ConnectionKind.Multiplexed);
            lock (_writeLock)
            {
                Volatile.Write(ref _session, null);
                _lastError = error;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            Volatile.Write(ref _state, (int)ConnectionState.Reconnecting);
            Log.ConnectionLost(_logger, nameof(ConnectionKind.Multiplexed), connection.Id, Endpoint, error);
            if (_options.BacklogPolicy == BacklogPolicy.FailFast)
            {
                FailBacklog(new RedisConnectionException($"The connection to {Endpoint} was lost.", error, mayHaveExecuted: false));
            }
        }
    }

    private void FailBacklogAfterConnectError(Exception error)
    {
        // Wrong credentials will not fix themselves within a command timeout; fail waiting commands right away.
        if (error is RedisAuthenticationException)
        {
            FailBacklog(error);
        }
        else if (_options.BacklogPolicy == BacklogPolicy.FailFast && _everConnected)
        {
            FailBacklog(new RedisConnectionException($"The connection to {Endpoint} is down.", error, mayHaveExecuted: false));
        }
    }

    private void FailBacklog(Exception error)
    {
        List<PendingOperation> failed;
        lock (_writeLock)
        {
            failed = [.. _writeQueue];
            _writeQueue.Clear();
        }

        foreach (var operation in failed)
        {
            operation.TryFailUnwritten(error);
        }
    }

    private async Task HeartbeatLoopAsync()
    {
        var period = TimeSpan.FromMilliseconds(Math.Clamp(_options.CommandTimeout.TotalMilliseconds / 5, 10, 100));
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(_shutdown.Token).ConfigureAwait(false))
            {
                var now = Environment.TickCount64;
                ExpireBacklog(now);
                var session = Volatile.Read(ref _session);
                if (session is not null)
                {
                    session.ExpirePending(now);
                    session.CheckKeepAlive(now);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ExpireBacklog(long now)
    {
        lock (_writeLock)
        {
            var expired = 0;
            foreach (var operation in _writeQueue)
            {
                if (operation.TryExpire(now))
                {
                    expired++;
                    _instrumentation.Timeout(operation.Name);
                }
            }

            if (expired > 0)
            {
                var alive = _writeQueue.Where(o => !o.IsCompleted).ToList();
                _writeQueue.Clear();
                foreach (var operation in alive)
                {
                    _writeQueue.Enqueue(operation);
                }
            }
        }
    }

    /// <summary>Takes up to <see cref="MaxBatchSize"/> queued operations.</summary>
    private int DequeueBatch(List<PendingOperation> batch)
    {
        batch.Clear();
        lock (_writeLock)
        {
            while (batch.Count < MaxBatchSize && _writeQueue.TryDequeue(out var operation))
            {
                batch.Add(operation);
            }
        }

        return batch.Count;
    }

    /// <summary>One physical connection with its reader and writer loops and its queue of awaited replies.</summary>
    private sealed class Session
    {
        private readonly MultiplexedConnection _owner;
        private readonly CancellationTokenSource _stop;
        private readonly object _pendingLock = new();
        private readonly Queue<PendingOperation> _pending = new();
        private Exception? _failure;
        private long _lastReadTicks = Environment.TickCount64;
        private PendingCommand<bool>? _keepAlive;

        public Session(MultiplexedConnection owner, PhysicalConnection connection)
        {
            _owner = owner;
            Connection = connection;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(owner._shutdown.Token);
        }

        public PhysicalConnection Connection { get; }

        public int PendingCount
        {
            get
            {
                lock (_pendingLock)
                {
                    return _pending.Count;
                }
            }
        }

        public async Task<Exception> RunAsync()
        {
            var writer = Task.Run(WriterLoopAsync);
            var reader = Task.Run(ReaderLoopAsync);
            var first = await Task.WhenAny(writer, reader).ConfigureAwait(false);
            var error = first.Exception?.GetBaseException()
                ?? Volatile.Read(ref _failure)
                ?? new RedisConnectionException($"The connection to {Connection.EndpointText} was closed.");
            if (error is RedisProtocolException)
            {
                Log.ProtocolError(_owner._logger, Connection.Id, Connection.EndpointText, error);
            }

            Fail(error);
            await Task.WhenAll(writer, reader).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await Connection.DisposeAsync().ConfigureAwait(false);
            _stop.Dispose();
            return Volatile.Read(ref _failure) ?? error;
        }

        /// <summary>Stops the session and fails every command waiting for a reply.</summary>
        public void Fail(Exception reason)
        {
            List<PendingOperation> pending;
            lock (_pendingLock)
            {
                if (_failure is not null)
                {
                    return;
                }

                _failure = reason;
                pending = [.. _pending];
                _pending.Clear();
            }

            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            Connection.Abort();
            if (pending.Count == 0)
            {
                return;
            }

            var lost = reason is ObjectDisposedException
                ? reason
                : new RedisConnectionException(
                    $"The connection to {Connection.EndpointText} was lost before the reply was received; the command may have been executed.",
                    reason,
                    mayHaveExecuted: true);
            foreach (var operation in pending)
            {
                operation.TrySetException(lost);
            }
        }

        public void ExpirePending(long now)
        {
            lock (_pendingLock)
            {
                foreach (var operation in _pending)
                {
                    if (operation.TryExpire(now))
                    {
                        _owner._instrumentation.Timeout(operation.Name);
                        Log.CommandAbandoned(_owner._logger, operation.Name, Connection.Id);
                    }
                }
            }
        }

        public void CheckKeepAlive(long now)
        {
            var keepAlive = Volatile.Read(ref _keepAlive);
            if (keepAlive is not null)
            {
                if (!keepAlive.IsCompleted)
                {
                    return;
                }

                Volatile.Write(ref _keepAlive, null);
                if (keepAlive.Task.Exception?.GetBaseException() is RedisTimeoutException)
                {
                    Log.KeepAliveTimedOut(_owner._logger, Connection.Id, Connection.EndpointText);
                    Fail(new RedisConnectionException($"Keep-alive PING to {Connection.EndpointText} timed out."));
                    return;
                }
            }

            // Two triggers for a health PING:
            //  - idle: nothing read for KeepAliveInterval (detects dead idle connections);
            //  - stall: replies are awaited but nothing was read for 2 x CommandTimeout. A half-open connection
            //    (peer gone, e.g. behind a proxy or NAT) accepts writes but never answers; without this check it
            //    would only be noticed after KeepAliveInterval while every command times out.
            var idle = now - Interlocked.Read(ref _lastReadTicks);
            var stalled = idle >= 2 * _owner._options.CommandTimeout.TotalMilliseconds && PendingCount > 0;
            if (stalled || idle >= _owner._options.KeepAliveInterval.TotalMilliseconds)
            {
                var ping = new PendingCommand<bool>(new RedisCommand(Commands.Ping), static _ => true, _owner._options.CommandTimeout);
                Volatile.Write(ref _keepAlive, ping);
                _owner.Enqueue(ping);
            }
        }

        public async Task WaitForPendingAsync(TimeSpan timeout)
        {
            var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (PendingCount > 0 && Volatile.Read(ref _failure) is null && Environment.TickCount64 < deadline)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        private void AddPending(PendingOperation operation)
        {
            lock (_pendingLock)
            {
                if (_failure is null)
                {
                    _pending.Enqueue(operation);
                    return;
                }
            }

            operation.TrySetException(new RedisConnectionException(
                $"The connection to {Connection.EndpointText} was lost while the command was being sent; it may have been executed.",
                _failure,
                mayHaveExecuted: true));
        }

        private async Task WriterLoopAsync()
        {
            var token = _stop.Token;
            var batch = new List<PendingOperation>(MaxBatchSize);
            while (true)
            {
                // Reset the signal before draining so that a concurrent enqueue always wakes us up again.
                Interlocked.Exchange(ref _owner._writeSignaled, 0);
                while (_owner.DequeueBatch(batch) > 0)
                {
                    foreach (var operation in batch)
                    {
                        if (!operation.TryMarkWritten())
                        {
                            continue;
                        }

                        operation.Write(Connection);
                        AddPending(operation);
                    }

                    await Connection.FlushAsync(token).ConfigureAwait(false);
                }

                await _owner._writeSignal.WaitAsync(token).ConfigureAwait(false);
            }
        }

        private async Task ReaderLoopAsync()
        {
            var token = _stop.Token;
            var reader = Connection.Reader;
            var scanner = Connection.Scanner;
            while (true)
            {
                var result = await reader.ReadAsync(token).ConfigureAwait(false);
                Interlocked.Exchange(ref _lastReadTicks, Environment.TickCount64);
                var buffer = result.Buffer;
                while (scanner.TryScan(buffer, out var length))
                {
                    var frame = RespFrame.Read(buffer.Slice(0, length));
                    if (frame.Type == RespType.Push)
                    {
                        _owner.PushHandler?.Invoke(frame);
                    }
                    else if (!Dispatch(frame))
                    {
                        reader.AdvanceTo(buffer.Start);
                        return;
                    }

                    buffer = buffer.Slice(length);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    throw new RedisConnectionException($"The connection to {Connection.EndpointText} was closed by the server.");
                }
            }
        }

        /// <summary>Delivers a reply to the oldest waiting operation; returns <see langword="false"/> if the session failed.</summary>
        private bool Dispatch(RespFrame frame)
        {
            PendingOperation? operation;
            lock (_pendingLock)
            {
                if (_failure is not null)
                {
                    return false;
                }

                if (!_pending.TryPeek(out operation))
                {
                    throw new RedisProtocolException("Received a reply while no command was waiting for one.");
                }
            }

            if (operation.OnFrame(frame))
            {
                lock (_pendingLock)
                {
                    if (_failure is not null)
                    {
                        return false;
                    }

                    _pending.Dequeue();
                }
            }

            return true;
        }
    }
}
