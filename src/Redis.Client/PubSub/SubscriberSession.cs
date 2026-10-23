using Microsoft.Extensions.Logging;
using Redis.Client.Connections;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;

namespace Redis.Client.PubSub;

/// <summary>
/// One physical Pub/Sub connection. Commands (SUBSCRIBE, UNSUBSCRIBE, PING) are confirmed in order; messages are
/// routed to local subscriptions. Works with RESP2 (arrays) and RESP3 (push frames).
/// </summary>
internal sealed class SubscriberSession
{
    private readonly RedisSubscriber _owner;
    private readonly RedisClientOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _pendingLock = new();
    private readonly Queue<Confirmation> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<Exception> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _failure;
    private long _lastReadTicks = Environment.TickCount64;

    public SubscriberSession(RedisSubscriber owner, PhysicalConnection connection, RedisClientOptions options, ILogger logger)
    {
        _owner = owner;
        Connection = connection;
        _options = options;
        _logger = logger;
    }

    public PhysicalConnection Connection { get; }

    /// <summary>Completes with the failure reason when the session ends.</summary>
    public Task<Exception> Completion => _completion.Task;

    public void Start()
    {
        _ = Task.Run(RunAsync);
    }

    /// <summary>Sends a subscription command for one channel/pattern (or PING) and waits for its confirmation.</summary>
    public async Task SendAsync(CommandMetadata command, string? name, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var confirmation = new Confirmation(command == Commands.Ping ? "pong" : command.Name.ToLowerInvariant());
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_pendingLock)
            {
                if (_failure is not null)
                {
                    throw new RedisConnectionException("The Pub/Sub connection was lost.", _failure);
                }

                _pending.Enqueue(confirmation);
            }

            Connection.Write(name is null ? new RedisCommand(command) : new RedisCommand(command, name));
            await Connection.FlushAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            throw new RedisConnectionException("The Pub/Sub connection was lost.", _failure);
        }
        finally
        {
            _writeLock.Release();
        }

        try
        {
            await confirmation.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new RedisTimeoutException($"{command.Name} was not confirmed within {timeout}.", mayHaveExecuted: true);
        }
    }

    public void Fail(Exception reason)
    {
        List<Confirmation> pending;
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
        var lost = new RedisConnectionException("The Pub/Sub connection was lost.", reason, mayHaveExecuted: true);
        foreach (var confirmation in pending)
        {
            confirmation.Completion.TrySetException(lost);
        }
    }

    private async Task RunAsync()
    {
        var keepAlive = Task.Run(KeepAliveLoopAsync);
        Exception error;
        try
        {
            await ReadLoopAsync().ConfigureAwait(false);
            error = new RedisConnectionException("The Pub/Sub connection was closed.");
        }
        catch (Exception ex)
        {
            error = _failure ?? ex;
            if (ex is RedisProtocolException)
            {
                Log.ProtocolError(_logger, Connection.Id, Connection.EndpointText, ex);
            }
        }

        Fail(error);
        try
        {
            await keepAlive.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await Connection.DisposeAsync().ConfigureAwait(false);
            _stop.Dispose();
        }
        finally
        {
            _completion.TrySetResult(_failure ?? error);
        }
    }

    private async Task ReadLoopAsync()
    {
        var token = _stop.Token;
        var reader = Connection.Reader;
        var deliveries = new List<(LocalSubscription Subscription, RedisMessage Message)>();
        while (true)
        {
            var result = await reader.ReadAsync(token).ConfigureAwait(false);
            Interlocked.Exchange(ref _lastReadTicks, Environment.TickCount64);
            var buffer = result.Buffer;
            while (Connection.Scanner.TryScan(buffer, out var length))
            {
                Handle(RespFrame.Read(buffer.Slice(0, length)), deliveries);
                buffer = buffer.Slice(length);
            }

            reader.AdvanceTo(buffer.Start, buffer.End);

            // Deliver after releasing the buffer: with the Wait policy this may block until consumers catch up.
            foreach (var (subscription, message) in deliveries)
            {
                await subscription.DeliverAsync(message, token).ConfigureAwait(false);
            }

            deliveries.Clear();
            if (result.IsCompleted)
            {
                throw new RedisConnectionException($"The Pub/Sub connection to {Connection.EndpointText} was closed by the server.");
            }
        }
    }

    private void Handle(RespFrame frame, List<(LocalSubscription, RedisMessage)> deliveries)
    {
        if (frame.IsError)
        {
            Confirm(null, frame.ToServerException());
            return;
        }

        if (frame.Type == RespType.SimpleString && frame.GetString() == "PONG")
        {
            Confirm("pong", null);
            return;
        }

        if (frame.Type is not (RespType.Push or RespType.Array) || frame.ChildCount == 0)
        {
            return;
        }

        var items = frame.ToArray();
        var kind = items[0].GetString();
        switch (kind)
        {
            case "message" when items.Length >= 3:
            {
                var channel = items[1].GetString();
                var message = new RedisMessage(channel, items[2].ToRedisValue(), null);
                foreach (var subscription in _owner.Lookup(channel, isPattern: false))
                {
                    deliveries.Add((subscription, message));
                }

                break;
            }

            case "pmessage" when items.Length >= 4:
            {
                var pattern = items[1].GetString();
                var message = new RedisMessage(items[2].GetString(), items[3].ToRedisValue(), pattern);
                foreach (var subscription in _owner.Lookup(pattern, isPattern: true))
                {
                    deliveries.Add((subscription, message));
                }

                break;
            }

            case "subscribe" or "psubscribe" or "unsubscribe" or "punsubscribe" or "pong":
                Confirm(kind, null);
                break;
        }
    }

    private void Confirm(string? kind, Exception? error)
    {
        Confirmation? confirmation;
        lock (_pendingLock)
        {
            if (!_pending.TryDequeue(out confirmation))
            {
                if (error is not null)
                {
                    throw new RedisProtocolException($"Unexpected error on the Pub/Sub connection: {error.Message}", error);
                }

                return; // e.g. a confirmation for a subscription restored twice
            }
        }

        if (error is not null)
        {
            confirmation.Completion.TrySetException(error);
        }
        else if (confirmation.Kind == kind)
        {
            confirmation.Completion.TrySetResult();
        }
        else
        {
            throw new RedisProtocolException($"Expected a '{confirmation.Kind}' confirmation but received '{kind}'.");
        }
    }

    private async Task KeepAliveLoopAsync()
    {
        var token = _stop.Token;
        var period = TimeSpan.FromMilliseconds(Math.Clamp(_options.KeepAliveInterval.TotalMilliseconds / 4, 10, 1000));
        using var timer = new PeriodicTimer(period);
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                var idle = Environment.TickCount64 - Interlocked.Read(ref _lastReadTicks);
                if (idle < _options.KeepAliveInterval.TotalMilliseconds)
                {
                    continue;
                }

                try
                {
                    await SendAsync(Commands.Ping, null, _options.CommandTimeout, token).ConfigureAwait(false);
                }
                catch (RedisTimeoutException)
                {
                    Log.KeepAliveTimedOut(_logger, Connection.Id, Connection.EndpointText);
                    Fail(new RedisConnectionException($"Keep-alive PING to {Connection.EndpointText} timed out."));
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (RedisConnectionException)
        {
        }
    }

    private sealed class Confirmation(string kind)
    {
        public string Kind { get; } = kind;

        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
