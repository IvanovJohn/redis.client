using Microsoft.Extensions.Logging;
using Redis.Client.Connections;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;

namespace Redis.Client.PubSub;

/// <summary>
/// Pub/Sub over one dedicated connection. Keeps a registry of local subscriptions, holds one server subscription
/// per channel or pattern, and restores all of them after reconnecting.
/// </summary>
internal sealed class RedisSubscriber : IRedisSubscriber, IAsyncDisposable
{
    private readonly RedisClientOptions _options;
    private readonly ILogger _logger;
    private readonly ClientInstrumentation _instrumentation;
    private readonly RedisClient _client;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _registryLock = new();
    private readonly Dictionary<string, List<LocalSubscription>> _channels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<LocalSubscription>> _patterns = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SubscriberSession? _session;
    private Exception? _lastError;
    private Task _connectionLoop = Task.CompletedTask;
    private int _started;
    private int _disposed;

    public RedisSubscriber(RedisClientOptions options, ILogger logger, ClientInstrumentation instrumentation, RedisClient client)
    {
        _options = options;
        _logger = logger;
        _instrumentation = instrumentation;
        _client = client;
    }

    /// <summary>The current physical connection (for tests).</summary>
    internal PhysicalConnection? Connection => Volatile.Read(ref _session)?.Connection;

    public ValueTask<IRedisSubscription> SubscribeAsync(string channel, CancellationToken cancellationToken = default) =>
        SubscribeCoreAsync(channel, isPattern: false, handler: null, cancellationToken);

    public ValueTask<IRedisSubscription> SubscribeAsync(string channel, Func<RedisMessage, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeCoreAsync(channel, isPattern: false, handler, cancellationToken);
    }

    public ValueTask<IRedisSubscription> SubscribePatternAsync(string pattern, CancellationToken cancellationToken = default) =>
        SubscribeCoreAsync(pattern, isPattern: true, handler: null, cancellationToken);

    public ValueTask<IRedisSubscription> SubscribePatternAsync(string pattern, Func<RedisMessage, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeCoreAsync(pattern, isPattern: true, handler, cancellationToken);
    }

    public ValueTask<long> PublishAsync(string channel, RedisValue message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        return _client.ExecuteCoreAsync(new RedisCommand(Commands.Publish, channel, message), ResultProcessors.Integer, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _shutdown.CancelAsync().ConfigureAwait(false);
        Volatile.Read(ref _session)?.Fail(new ObjectDisposedException(nameof(RedisClient)));
        await _connectionLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        LocalSubscription[] all;
        lock (_registryLock)
        {
            all = [.. _channels.Values.SelectMany(l => l), .. _patterns.Values.SelectMany(l => l)];
            _channels.Clear();
            _patterns.Clear();
        }

        foreach (var subscription in all)
        {
            subscription.Complete();
        }

        _shutdown.Dispose();
    }

    /// <summary>Removes a local subscription; the server subscription is dropped with the last local one.</summary>
    internal async Task UnsubscribeAsync(LocalSubscription subscription)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            bool last;
            lock (_registryLock)
            {
                var registry = subscription.IsPattern ? _patterns : _channels;
                if (!registry.TryGetValue(subscription.Name, out var list) || !list.Remove(subscription))
                {
                    return;
                }

                last = list.Count == 0;
                if (last)
                {
                    registry.Remove(subscription.Name);
                }
            }

            if (last && Volatile.Read(ref _session) is { } session)
            {
                try
                {
                    await session.SendAsync(subscription.IsPattern ? Commands.PUnsubscribe : Commands.Unsubscribe, subscription.Name, _options.CommandTimeout, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is RedisException or OperationCanceledException or ObjectDisposedException)
                {
                    // The connection is gone: the server-side subscription went with it and will not be restored.
                }
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>Local subscriptions for a channel or pattern (snapshot).</summary>
    internal LocalSubscription[] Lookup(string name, bool isPattern)
    {
        lock (_registryLock)
        {
            var registry = isPattern ? _patterns : _channels;
            return registry.TryGetValue(name, out var list) ? [.. list] : [];
        }
    }

    private async ValueTask<IRedisSubscription> SubscribeCoreAsync(string name, bool isPattern, Func<RedisMessage, CancellationToken, ValueTask>? handler, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var subscription = new LocalSubscription(this, name, isPattern, _options, _instrumentation);
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool first;
            lock (_registryLock)
            {
                var registry = isPattern ? _patterns : _channels;
                if (!registry.TryGetValue(name, out var list))
                {
                    registry[name] = list = [];
                }

                first = list.Count == 0;
                list.Add(subscription);
            }

            if (first)
            {
                try
                {
                    var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
                    await session.SendAsync(isPattern ? Commands.PSubscribe : Commands.Subscribe, name, _options.CommandTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    lock (_registryLock)
                    {
                        var registry = isPattern ? _patterns : _channels;
                        if (registry.TryGetValue(name, out var list) && list.Remove(subscription) && list.Count == 0)
                        {
                            registry.Remove(name);
                        }
                    }

                    subscription.Complete();
                    throw;
                }
            }
        }
        finally
        {
            _operationLock.Release();
        }

        if (handler is not null)
        {
            subscription.StartHandler(handler, _logger);
        }

        return subscription;
    }

    private async Task<SubscriberSession> GetSessionAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) == 0)
        {
            _connectionLoop = Task.Run(ConnectionLoopAsync);
        }

        while (true)
        {
            if (Volatile.Read(ref _session) is { } session)
            {
                return session;
            }

            try
            {
                await Volatile.Read(ref _connected).Task.WaitAsync(_options.ConnectTimeout + _options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new RedisConnectionException(
                    $"The Pub/Sub connection to {RedisClientOptions.FormatEndpoint(_options.Endpoints[0])} is not available.", _lastError);
            }
        }
    }

    private async Task ConnectionLoopAsync()
    {
        var token = _shutdown.Token;
        var attempt = 0;
        var everConnected = false;
        var endpoint = RedisClientOptions.FormatEndpoint(_options.Endpoints[0]);
        while (!token.IsCancellationRequested)
        {
            SubscriberSession session;
            try
            {
                var connection = await PhysicalConnection.ConnectAsync(_options, ConnectionKind.Subscriber, _logger, token).ConfigureAwait(false);
                session = new SubscriberSession(this, connection, _options, _logger);
                session.Start();
                var restored = await RestoreAsync(session, token).ConfigureAwait(false);
                if (everConnected)
                {
                    _instrumentation.Reconnected();
                    Log.Reconnected(_logger, nameof(ConnectionKind.Subscriber), endpoint, attempt + 1);
                    Log.SubscriptionsRestored(_logger, restored, endpoint);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                _lastError = ex;
                var delay = Backoff.GetDelay(attempt, _options.ReconnectBaseDelay, _options.ReconnectMaxDelay);
                Log.ConnectFailed(_logger, endpoint, attempt, delay, ex);
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

            attempt = 0;
            Volatile.Write(ref _session, session);
            _instrumentation.ConnectionOpened(ConnectionKind.Subscriber);
            Volatile.Read(ref _connected).TrySetResult();
            if (everConnected)
            {
                Notify(static s => s.OnConnectionRestored());
            }

            everConnected = true;
            Exception error;
            try
            {
                error = await session.Completion.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            Volatile.Write(ref _session, null);
            Interlocked.Exchange(ref _connected, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _instrumentation.ConnectionClosed(ConnectionKind.Subscriber);
            _lastError = error;
            if (token.IsCancellationRequested)
            {
                return;
            }

            Log.ConnectionLost(_logger, nameof(ConnectionKind.Subscriber), session.Connection.Id, endpoint, error);
            Notify(static s => s.OnConnectionLost());
        }
    }

    /// <summary>Re-subscribes every channel and pattern in the registry on a new connection.</summary>
    private async Task<int> RestoreAsync(SubscriberSession session, CancellationToken cancellationToken)
    {
        string[] channels;
        string[] patterns;
        lock (_registryLock)
        {
            channels = [.. _channels.Keys];
            patterns = [.. _patterns.Keys];
        }

        try
        {
            foreach (var channel in channels)
            {
                await session.SendAsync(Commands.Subscribe, channel, _options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            }

            foreach (var pattern in patterns)
            {
                await session.SendAsync(Commands.PSubscribe, pattern, _options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            session.Fail(ex);
            await session.Completion.ConfigureAwait(false);
            throw;
        }

        return channels.Length + patterns.Length;
    }

    private void Notify(Action<LocalSubscription> action)
    {
        LocalSubscription[] all;
        lock (_registryLock)
        {
            all = [.. _channels.Values.SelectMany(l => l), .. _patterns.Values.SelectMany(l => l)];
        }

        foreach (var subscription in all)
        {
            try
            {
                action(subscription);
            }
            catch (Exception ex)
            {
                Log.SubscriptionHandlerFailed(_logger, subscription.Name, ex);
            }
        }
    }
}
