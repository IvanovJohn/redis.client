using Redis.Client.CommandGroups;
using Redis.Client.Connections;
using Redis.Client.Protocol;
using Redis.Client.Transactions;

namespace Redis.Client;

/// <summary>
/// A <c>MULTI</c>/<c>EXEC</c> transaction. Calling a command method queues the command and returns a task that
/// completes after <see cref="ExecuteAsync(CancellationToken)"/>; do not await those tasks before executing.
/// </summary>
public interface IRedisTransaction : IRedisCommands
{
    /// <summary>
    /// Sends the queued commands as one block and waits for <c>EXEC</c>. Results (or per-command errors such as
    /// <c>WRONGTYPE</c>) are delivered through the tasks returned when the commands were queued.
    /// </summary>
    /// <param name="cancellationToken">Cancels waiting; commands already sent may still be executed.</param>
    /// <returns>A task that completes when the transaction has been executed.</returns>
    /// <exception cref="RedisServerException">The transaction was rejected (<c>EXECABORT</c>); nothing was executed.</exception>
    /// <exception cref="InvalidOperationException">The transaction was already executed.</exception>
    ValueTask ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// An optimistic transaction on a dedicated connection with <c>WATCH</c>ed keys. Dispose it to release the connection.
/// </summary>
public interface IRedisWatchedTransaction : IRedisCommands, IAsyncDisposable
{
    /// <summary>Gets commands that run immediately on the transaction's connection, e.g. to read watched values.</summary>
    IRedisCommands Connection { get; }

    /// <summary>Executes the queued commands with <c>MULTI</c>/<c>EXEC</c>.</summary>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>
    /// <see langword="true"/> if the transaction was executed; <see langword="false"/> if a watched key was changed by
    /// someone else, in which case nothing was executed and the queued command tasks are cancelled.
    /// </returns>
    /// <exception cref="RedisServerException">The transaction was rejected (<c>EXECABORT</c>).</exception>
    ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Collects commands of a transaction instead of sending them.</summary>
internal sealed class TransactionCollector : ICommandExecutor
{
    private readonly List<QueuedCommand> _commands = [];
    private bool _sealed;

    public IReadOnlyList<QueuedCommand> Seal()
    {
        lock (_commands)
        {
            if (_sealed)
            {
                throw new InvalidOperationException("The transaction has already been executed.");
            }

            _sealed = true;
            return [.. _commands];
        }
    }

    public ValueTask<T> ExecuteAsync<T>(RedisCommand command, Func<RespFrame, T> processor, CancellationToken cancellationToken)
    {
        if (command.Metadata == Commands.Scan || command.Metadata == Commands.HScan || command.Metadata == Commands.SScan || command.Metadata == Commands.ZScan)
        {
            throw new NotSupportedException("SCAN iterations cannot run inside a transaction.");
        }

        var queued = new QueuedCommand<T>(command, processor);
        lock (_commands)
        {
            if (_sealed)
            {
                throw new InvalidOperationException("The transaction has already been executed.");
            }

            _commands.Add(queued);
        }

        return new ValueTask<T>(queued.Task);
    }
}

/// <summary>Base for transactions: command groups that queue into a <see cref="TransactionCollector"/>.</summary>
internal abstract class TransactionBase : IRedisCommands
{
    protected TransactionBase()
    {
        Collector = new TransactionCollector();
        Keys = new KeyCommands(Collector);
        Strings = new StringCommands(Collector);
        Hashes = new HashCommands(Collector);
        Lists = new ListCommands(Collector);
        Sets = new SetCommands(Collector);
        SortedSets = new SortedSetCommands(Collector);
    }

    public IKeyCommands Keys { get; }

    public IStringCommands Strings { get; }

    public IHashCommands Hashes { get; }

    public IListCommands Lists { get; }

    public ISetCommands Sets { get; }

    public ISortedSetCommands SortedSets { get; }

    protected TransactionCollector Collector { get; }

    public ValueTask<RedisResult> ExecuteAsync(string command, IReadOnlyList<RedisValue>? arguments = null, CancellationToken cancellationToken = default) =>
        Collector.ExecuteAsync(RedisClient.CreateGenericCommand(command, arguments), RedisResult.From, cancellationToken);
}

/// <summary>A transaction sent through a multiplexed connection.</summary>
internal sealed class RedisTransaction(RedisClient client) : TransactionBase, IRedisTransaction
{
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var commands = Collector.Seal();
        if (commands.Count == 0)
        {
            return;
        }

        var operation = new PendingTransaction(commands, client.Options.CommandTimeout);
        operation.RegisterCancellation(cancellationToken);
        if (client.IsInstrumented)
        {
            await client.Instrumented("EXEC", null, () => Run(operation), commands.Count).ConfigureAwait(false);
        }
        else
        {
            await Run(operation).ConfigureAwait(false);
        }
    }

    private Task<bool> Run(PendingTransaction operation)
    {
        client.Router.Enqueue(operation);
        return operation.Task;
    }
}

/// <summary>A WATCH transaction that owns a dedicated connection until disposed.</summary>
internal sealed class RedisWatchedTransaction : TransactionBase, IRedisWatchedTransaction
{
    private readonly RedisClient _client;
    private readonly DedicatedConnectionLease _lease;
    private readonly DedicatedExecutor _connectionExecutor;
    private int _state; // 0 open, 1 executed, 2 disposed

    public RedisWatchedTransaction(RedisClient client, DedicatedConnectionLease lease)
    {
        _client = client;
        _lease = lease;
        _connectionExecutor = new DedicatedExecutor(client, lease);
        Connection = new DirectCommands(_connectionExecutor);
    }

    public IRedisCommands Connection { get; }

    public async ValueTask<bool> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _state) == 2, this);
        var commands = Collector.Seal();
        Volatile.Write(ref _state, 1);
        var operation = new PendingTransaction(commands, _client.Options.CommandTimeout);
        return _client.IsInstrumented
            ? await _client.Instrumented("EXEC", null, () => _lease.ExecuteOperationAsync(operation, cancellationToken), commands.Count).ConfigureAwait(false)
            : await _lease.ExecuteOperationAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var previous = Interlocked.Exchange(ref _state, 2);
        if (previous == 2)
        {
            return;
        }

        if (previous == 0)
        {
            // Not executed: drop the WATCH so the pooled connection starts clean.
            try
            {
                await _lease.ExecuteAsync(new RedisCommand(Commands.Unwatch), ResultProcessors.Ok, _client.Options.CommandTimeout, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _lease.MarkBroken();
            }
        }

        await _lease.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Runs commands immediately on a leased dedicated connection.</summary>
internal sealed class DedicatedExecutor(RedisClient client, DedicatedConnectionLease lease) : ICommandExecutor
{
    public ValueTask<T> ExecuteAsync<T>(RedisCommand command, Func<RespFrame, T> processor, CancellationToken cancellationToken)
    {
        var timeout = command.GetBlockingTimeout() switch
        {
            null => client.Options.CommandTimeout,
            var t when t == Timeout.InfiniteTimeSpan => Timeout.InfiniteTimeSpan,
            var t => t.Value + client.Options.CommandTimeout,
        };
        return client.IsInstrumented
            ? client.Instrumented(command.Metadata.Name, command, () => lease.ExecuteAsync(command, processor, timeout, cancellationToken))
            : new ValueTask<T>(lease.ExecuteAsync(command, processor, timeout, cancellationToken));
    }
}

/// <summary>Command groups bound to an executor that sends immediately.</summary>
internal sealed class DirectCommands(ICommandExecutor executor) : IRedisCommands
{
    public IKeyCommands Keys { get; } = new KeyCommands(executor);

    public IStringCommands Strings { get; } = new StringCommands(executor);

    public IHashCommands Hashes { get; } = new HashCommands(executor);

    public IListCommands Lists { get; } = new ListCommands(executor);

    public ISetCommands Sets { get; } = new SetCommands(executor);

    public ISortedSetCommands SortedSets { get; } = new SortedSetCommands(executor);

    public ValueTask<RedisResult> ExecuteAsync(string command, IReadOnlyList<RedisValue>? arguments = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(RedisClient.CreateGenericCommand(command, arguments), RedisResult.From, cancellationToken);
}
