using System.Runtime.CompilerServices;
using Redis.Client.CommandGroups;
using Redis.Client.Protocol;

namespace Redis.Client;

/// <summary>Commands for sets.</summary>
public interface ISetCommands
{
    /// <summary>Adds a member (<c>SADD</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the member was added.</returns>
    ValueTask<bool> AddAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default);

    /// <summary>Adds members (<c>SADD</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="members">The members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of members added.</returns>
    ValueTask<long> AddAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default);

    /// <summary>Removes a member (<c>SREM</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the member was removed.</returns>
    ValueTask<bool> RemoveAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default);

    /// <summary>Removes members (<c>SREM</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="members">The members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of members removed.</returns>
    ValueTask<long> RemoveAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default);

    /// <summary>Returns all members (<c>SMEMBERS</c>). For large sets prefer <see cref="ScanAsync"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The members.</returns>
    ValueTask<RedisValue[]> MembersAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Checks membership (<c>SISMEMBER</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the member is in the set.</returns>
    ValueTask<bool> ContainsAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default);

    /// <summary>Checks membership of several members (<c>SMISMEMBER</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="members">The members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>One flag per member.</returns>
    ValueTask<bool[]> ContainsAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default);

    /// <summary>Returns the number of members (<c>SCARD</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of members.</returns>
    ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns a random member (<c>SPOP</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The member, or <see cref="RedisValue.Null"/> if the set is empty.</returns>
    ValueTask<RedisValue> PopAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns up to <paramref name="count"/> random members (<c>SPOP key count</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The maximum number of members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The members.</returns>
    ValueTask<RedisValue[]> PopAsync(RedisKey key, long count, CancellationToken cancellationToken = default);

    /// <summary>Returns a random member without removing it (<c>SRANDMEMBER</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The member, or <see cref="RedisValue.Null"/> if the set is empty.</returns>
    ValueTask<RedisValue> RandomMemberAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Returns random members (<c>SRANDMEMBER key count</c>); a negative count allows repetitions.</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The number of members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The members.</returns>
    ValueTask<RedisValue[]> RandomMembersAsync(RedisKey key, long count, CancellationToken cancellationToken = default);

    /// <summary>Atomically moves a member to another set (<c>SMOVE</c>).</summary>
    /// <param name="source">The source set.</param>
    /// <param name="destination">The destination set.</param>
    /// <param name="member">The member.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the member was moved.</returns>
    ValueTask<bool> MoveAsync(RedisKey source, RedisKey destination, RedisValue member, CancellationToken cancellationToken = default);

    /// <summary>Computes a union, intersection or difference (<c>SUNION</c>/<c>SINTER</c>/<c>SDIFF</c>).</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="keys">The sets; for <see cref="SetOperation.Difference"/> the first one is the base.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The resulting members.</returns>
    ValueTask<RedisValue[]> CombineAsync(SetOperation operation, IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default);

    /// <summary>Stores a union, intersection or difference (<c>SUNIONSTORE</c>/<c>SINTERSTORE</c>/<c>SDIFFSTORE</c>).</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="destination">Where to store the result (overwritten).</param>
    /// <param name="keys">The sets.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of members in the result.</returns>
    ValueTask<long> CombineAndStoreAsync(SetOperation operation, RedisKey destination, IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default);

    /// <summary>Iterates over the members of a set (<c>SSCAN</c>) without blocking the server.</summary>
    /// <param name="key">The key.</param>
    /// <param name="pattern">Glob-style pattern (<c>MATCH</c>).</param>
    /// <param name="pageSize">Hint for the amount of work per call (<c>COUNT</c>).</param>
    /// <param name="cancellationToken">Stops the iteration.</param>
    /// <returns>The members.</returns>
    IAsyncEnumerable<RedisValue> ScanAsync(RedisKey key, string? pattern = null, int? pageSize = null, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="ISetCommands"/> over a command executor.</summary>
internal sealed class SetCommands(ICommandExecutor executor) : ISetCommands
{
    public ValueTask<bool> AddAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SAdd, Args.Key(key), member), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> AddAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SAdd, Args.KeyAndValues(key, members)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<bool> RemoveAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SRem, Args.Key(key), member), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> RemoveAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SRem, Args.KeyAndValues(key, members)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<RedisValue[]> MembersAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SMembers, Args.Key(key)), ResultProcessors.Values, cancellationToken);

    public ValueTask<bool> ContainsAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SIsMember, Args.Key(key), member), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<bool[]> ContainsAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SMIsMember, Args.KeyAndValues(key, members)), ResultProcessors.Booleans, cancellationToken);

    public ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SCard, Args.Key(key)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<RedisValue> PopAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SPop, Args.Key(key)), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue[]> PopAsync(RedisKey key, long count, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SPop, Args.Key(key), count), ResultProcessors.Values, cancellationToken);

    public ValueTask<RedisValue> RandomMemberAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SRandMember, Args.Key(key)), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue[]> RandomMembersAsync(RedisKey key, long count, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SRandMember, Args.Key(key), count), ResultProcessors.Values, cancellationToken);

    public ValueTask<bool> MoveAsync(RedisKey source, RedisKey destination, RedisValue member, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SMove, Args.Key(source), Args.Key(destination), member), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<RedisValue[]> CombineAsync(SetOperation operation, IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default)
    {
        Args.NotEmpty(keys, nameof(keys));
        var metadata = operation switch
        {
            SetOperation.Union => Commands.SUnion,
            SetOperation.Intersect => Commands.SInter,
            SetOperation.Difference => Commands.SDiff,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
        return executor.ExecuteAsync(new RedisCommand(metadata, Args.Keys(keys)), ResultProcessors.Values, cancellationToken);
    }

    public ValueTask<long> CombineAndStoreAsync(SetOperation operation, RedisKey destination, IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default)
    {
        Args.NotEmpty(keys, nameof(keys));
        var metadata = operation switch
        {
            SetOperation.Union => Commands.SUnionStore,
            SetOperation.Intersect => Commands.SInterStore,
            SetOperation.Difference => Commands.SDiffStore,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };
        var args = Args.Keys(keys, prefix: 1);
        args[0] = Args.Key(destination);
        return executor.ExecuteAsync(new RedisCommand(metadata, args), ResultProcessors.Integer, cancellationToken);
    }

    public async IAsyncEnumerable<RedisValue> ScanAsync(RedisKey key, string? pattern = null, int? pageSize = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RedisValue cursor = "0";
        do
        {
            var args = new List<RedisValue> { Args.Key(key), cursor };
            KeyCommands.ScanOptions(args, pattern, pageSize);
            var (next, items) = await executor.ExecuteAsync(new RedisCommand(Commands.SScan, [.. args]), ResultProcessors.Scan, cancellationToken).ConfigureAwait(false);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            cursor = next;
        }
        while ((string?)cursor != "0");
    }
}
