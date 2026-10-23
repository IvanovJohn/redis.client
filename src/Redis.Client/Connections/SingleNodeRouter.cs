using Microsoft.Extensions.Logging;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;

namespace Redis.Client.Connections;

/// <summary>
/// Routes commands to one Redis server: non-blocking commands are spread round-robin over
/// <see cref="RedisClientOptions.MultiplexedConnectionCount"/> multiplexed connections, blocking commands go to the
/// dedicated pool.
/// </summary>
internal sealed class SingleNodeRouter : ICommandRouter
{
    private readonly RedisClientOptions _options;
    private readonly MultiplexedConnection[] _multiplexed;
    private int _next = -1;

    public SingleNodeRouter(RedisClientOptions options, ILogger logger, ClientInstrumentation instrumentation)
    {
        _options = options;
        _multiplexed = new MultiplexedConnection[options.MultiplexedConnectionCount];
        for (var i = 0; i < _multiplexed.Length; i++)
        {
            _multiplexed[i] = new MultiplexedConnection(options, logger, instrumentation);
        }

        Dedicated = new DedicatedConnectionPool(options, logger, instrumentation);
    }

    public IReadOnlyList<MultiplexedConnection> Multiplexed => _multiplexed;

    public DedicatedConnectionPool Dedicated { get; }

    public long BacklogCount => _multiplexed.Sum(m => (long)m.BacklogCount);

    public long PendingCount => _multiplexed.Sum(m => (long)m.PendingCount);

    public Task ConnectAsync(CancellationToken cancellationToken) =>
        Task.WhenAll(_multiplexed.Select(m => m.ConnectAsync(cancellationToken)));

    public Task<T> ExecuteAsync<T>(RedisCommand command, Func<RespFrame, T> processor, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        if (command.IsBlocking)
        {
            return ExecuteBlockingAsync(command, processor, cancellationToken);
        }

        var operation = new PendingCommand<T>(command, processor, _options.CommandTimeout);
        operation.RegisterCancellation(cancellationToken);
        Next().Enqueue(operation);
        return operation.Task;
    }

    public void Enqueue(PendingOperation operation) => Next().Enqueue(operation);

    public ValueTask<DedicatedConnectionLease> LeaseDedicatedAsync(CancellationToken cancellationToken) => Dedicated.LeaseAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_multiplexed.Select(m => m.DisposeAsync().AsTask())).ConfigureAwait(false);
        await Dedicated.DisposeAsync().ConfigureAwait(false);
    }

    private MultiplexedConnection Next()
    {
        if (_multiplexed.Length == 1)
        {
            return _multiplexed[0];
        }

        var index = (int)((uint)Interlocked.Increment(ref _next) % (uint)_multiplexed.Length);
        return _multiplexed[index];
    }

    private async Task<T> ExecuteBlockingAsync<T>(RedisCommand command, Func<RespFrame, T> processor, CancellationToken cancellationToken)
    {
        // The server may legitimately wait for the whole blocking timeout; the client deadline adds CommandTimeout on top.
        var serverTimeout = command.GetBlockingTimeout() ?? TimeSpan.Zero;
        var timeout = serverTimeout == Timeout.InfiniteTimeSpan ? Timeout.InfiniteTimeSpan : serverTimeout + _options.CommandTimeout;
        await using var lease = await Dedicated.LeaseAsync(cancellationToken).ConfigureAwait(false);
        return await lease.ExecuteAsync(command, processor, timeout, cancellationToken).ConfigureAwait(false);
    }
}
