using Redis.Client.Protocol;

namespace Redis.Client.Connections;

/// <summary>
/// Chooses where a command runs. The single-node implementation sends non-blocking commands to a multiplexed
/// connection and blocking ones to the dedicated pool; a future cluster router will also use key positions
/// (<see cref="CommandMetadata"/>) to pick the node owning the hash slot.
/// </summary>
internal interface ICommandRouter : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Runs one command and converts its reply.</summary>
    Task<T> ExecuteAsync<T>(RedisCommand command, Func<RespFrame, T> processor, CancellationToken cancellationToken);

    /// <summary>Queues an operation (such as a transaction block) on a multiplexed connection.</summary>
    void Enqueue(PendingOperation operation);

    /// <summary>Leases a dedicated connection; dispose the lease to return it.</summary>
    ValueTask<DedicatedConnectionLease> LeaseDedicatedAsync(CancellationToken cancellationToken);
}
