using Redis.Client.CommandGroups;
using Redis.Client.Protocol;

namespace Redis.Client;

/// <summary>Commands for lists.</summary>
public interface IListCommands
{
    /// <summary>Prepends a value (<c>LPUSH</c>, or <c>LPUSHX</c> with <see cref="When.Exists"/>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="when"><see cref="When.Exists"/> pushes only to an existing list.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length of the list after the push (0 if nothing was pushed).</returns>
    ValueTask<long> LeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CancellationToken cancellationToken = default);

    /// <summary>Prepends values in order, so the last one ends up first (<c>LPUSH</c>/<c>LPUSHX</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <param name="when"><see cref="When.Exists"/> pushes only to an existing list.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length of the list after the push.</returns>
    ValueTask<long> LeftPushAsync(RedisKey key, IReadOnlyList<RedisValue> values, When when = When.Always, CancellationToken cancellationToken = default);

    /// <summary>Appends a value (<c>RPUSH</c>, or <c>RPUSHX</c> with <see cref="When.Exists"/>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="when"><see cref="When.Exists"/> pushes only to an existing list.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length of the list after the push (0 if nothing was pushed).</returns>
    ValueTask<long> RightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CancellationToken cancellationToken = default);

    /// <summary>Appends values in order (<c>RPUSH</c>/<c>RPUSHX</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <param name="when"><see cref="When.Exists"/> pushes only to an existing list.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length of the list after the push.</returns>
    ValueTask<long> RightPushAsync(RedisKey key, IReadOnlyList<RedisValue> values, When when = When.Always, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns the first element (<c>LPOP</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The element, or <see cref="RedisValue.Null"/> if the list is empty.</returns>
    ValueTask<RedisValue> LeftPopAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns up to <paramref name="count"/> first elements (<c>LPOP key count</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The maximum number of elements.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The elements; empty if the list is empty.</returns>
    ValueTask<RedisValue[]> LeftPopAsync(RedisKey key, long count, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns the last element (<c>RPOP</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The element, or <see cref="RedisValue.Null"/> if the list is empty.</returns>
    ValueTask<RedisValue> RightPopAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns up to <paramref name="count"/> last elements (<c>RPOP key count</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The maximum number of elements.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The elements, last first; empty if the list is empty.</returns>
    ValueTask<RedisValue[]> RightPopAsync(RedisKey key, long count, CancellationToken cancellationToken = default);

    /// <summary>Returns a range of elements (<c>LRANGE</c>); negative indexes count from the end.</summary>
    /// <param name="key">The key.</param>
    /// <param name="start">First index.</param>
    /// <param name="stop">Last index (inclusive).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The elements.</returns>
    ValueTask<RedisValue[]> RangeAsync(RedisKey key, long start = 0, long stop = -1, CancellationToken cancellationToken = default);

    /// <summary>Returns the length of a list (<c>LLEN</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length, or 0 if the key does not exist.</returns>
    ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Returns the element at an index (<c>LINDEX</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="index">The index; negative values count from the end.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The element, or <see cref="RedisValue.Null"/> if the index is out of range.</returns>
    ValueTask<RedisValue> GetByIndexAsync(RedisKey key, long index, CancellationToken cancellationToken = default);

    /// <summary>Replaces the element at an index (<c>LSET</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="index">The index.</param>
    /// <param name="value">The new value.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>A task that completes when the element is replaced.</returns>
    /// <exception cref="RedisServerException">The index is out of range.</exception>
    ValueTask SetByIndexAsync(RedisKey key, long index, RedisValue value, CancellationToken cancellationToken = default);

    /// <summary>Inserts a value before the first occurrence of <paramref name="pivot"/> (<c>LINSERT BEFORE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="pivot">The existing element.</param>
    /// <param name="value">The value to insert.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The new length, -1 if the pivot was not found, 0 if the key does not exist.</returns>
    ValueTask<long> InsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CancellationToken cancellationToken = default);

    /// <summary>Inserts a value after the first occurrence of <paramref name="pivot"/> (<c>LINSERT AFTER</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="pivot">The existing element.</param>
    /// <param name="value">The value to insert.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The new length, -1 if the pivot was not found, 0 if the key does not exist.</returns>
    ValueTask<long> InsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CancellationToken cancellationToken = default);

    /// <summary>Removes occurrences of a value (<c>LREM</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="count">0 removes all, a positive count removes from the head, a negative one from the tail.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of removed elements.</returns>
    ValueTask<long> RemoveAsync(RedisKey key, RedisValue value, long count = 0, CancellationToken cancellationToken = default);

    /// <summary>Keeps only a range of elements (<c>LTRIM</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="start">First index to keep.</param>
    /// <param name="stop">Last index to keep (inclusive).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>A task that completes when the list is trimmed.</returns>
    ValueTask TrimAsync(RedisKey key, long start, long stop, CancellationToken cancellationToken = default);

    /// <summary>Returns the index of a value (<c>LPOS</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="element">The value.</param>
    /// <param name="rank">Which match to return (1 = first, -1 = last).</param>
    /// <param name="maxLength">Compare at most this many elements (<c>MAXLEN</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The index, or <see langword="null"/> if not found.</returns>
    ValueTask<long?> PositionAsync(RedisKey key, RedisValue element, long? rank = null, long? maxLength = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the indexes of up to <paramref name="count"/> matches of a value (<c>LPOS ... COUNT</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="element">The value.</param>
    /// <param name="count">The maximum number of matches; 0 returns all.</param>
    /// <param name="rank">Which match to start from (1 = first, -1 = last).</param>
    /// <param name="maxLength">Compare at most this many elements (<c>MAXLEN</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The indexes.</returns>
    ValueTask<long[]> PositionsAsync(RedisKey key, RedisValue element, long count, long? rank = null, long? maxLength = null, CancellationToken cancellationToken = default);

    /// <summary>Atomically moves an element between lists (<c>LMOVE</c>).</summary>
    /// <param name="source">The source list.</param>
    /// <param name="destination">The destination list (may be the same).</param>
    /// <param name="from">The end of the source to take from.</param>
    /// <param name="to">The end of the destination to put to.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The moved element, or <see cref="RedisValue.Null"/> if the source is empty.</returns>
    ValueTask<RedisValue> MoveAsync(RedisKey source, RedisKey destination, ListSide from, ListSide to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes and returns the first element, waiting up to <paramref name="timeout"/> for one to arrive (<c>BLPOP</c>).
    /// Runs on a dedicated connection; the client deadline is the timeout plus <see cref="RedisClientOptions.CommandTimeout"/>.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="timeout">Maximum wait; <see cref="TimeSpan.Zero"/> waits forever.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The element, or <see cref="RedisValue.Null"/> if the timeout expired.</returns>
    ValueTask<RedisValue> BlockingLeftPopAsync(RedisKey key, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Waits for an element in any of the lists and removes it from the head (<c>BLPOP</c>).</summary>
    /// <param name="keys">The keys, checked in order.</param>
    /// <param name="timeout">Maximum wait; <see cref="TimeSpan.Zero"/> waits forever.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The key and element, or <see langword="null"/> if the timeout expired.</returns>
    ValueTask<(RedisKey Key, RedisValue Value)?> BlockingLeftPopAsync(IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns the last element, waiting up to <paramref name="timeout"/> (<c>BRPOP</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="timeout">Maximum wait; <see cref="TimeSpan.Zero"/> waits forever.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The element, or <see cref="RedisValue.Null"/> if the timeout expired.</returns>
    ValueTask<RedisValue> BlockingRightPopAsync(RedisKey key, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Waits for an element in any of the lists and removes it from the tail (<c>BRPOP</c>).</summary>
    /// <param name="keys">The keys, checked in order.</param>
    /// <param name="timeout">Maximum wait; <see cref="TimeSpan.Zero"/> waits forever.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The key and element, or <see langword="null"/> if the timeout expired.</returns>
    ValueTask<(RedisKey Key, RedisValue Value)?> BlockingRightPopAsync(IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Moves an element between lists, waiting for the source to have one (<c>BLMOVE</c>).</summary>
    /// <param name="source">The source list.</param>
    /// <param name="destination">The destination list.</param>
    /// <param name="from">The end of the source to take from.</param>
    /// <param name="to">The end of the destination to put to.</param>
    /// <param name="timeout">Maximum wait; <see cref="TimeSpan.Zero"/> waits forever.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The moved element, or <see cref="RedisValue.Null"/> if the timeout expired.</returns>
    ValueTask<RedisValue> BlockingMoveAsync(RedisKey source, RedisKey destination, ListSide from, ListSide to, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="IListCommands"/> over a command executor.</summary>
internal sealed class ListCommands(ICommandExecutor executor) : IListCommands
{
    public ValueTask<long> LeftPushAsync(RedisKey key, RedisValue value, When when = When.Always, CancellationToken cancellationToken = default) =>
        Push(true, key, [value], when, cancellationToken);

    public ValueTask<long> LeftPushAsync(RedisKey key, IReadOnlyList<RedisValue> values, When when = When.Always, CancellationToken cancellationToken = default) =>
        Push(true, key, values, when, cancellationToken);

    public ValueTask<long> RightPushAsync(RedisKey key, RedisValue value, When when = When.Always, CancellationToken cancellationToken = default) =>
        Push(false, key, [value], when, cancellationToken);

    public ValueTask<long> RightPushAsync(RedisKey key, IReadOnlyList<RedisValue> values, When when = When.Always, CancellationToken cancellationToken = default) =>
        Push(false, key, values, when, cancellationToken);

    public ValueTask<RedisValue> LeftPopAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LPop, Args.Key(key)), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue[]> LeftPopAsync(RedisKey key, long count, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LPop, Args.Key(key), count), ResultProcessors.Values, cancellationToken);

    public ValueTask<RedisValue> RightPopAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.RPop, Args.Key(key)), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue[]> RightPopAsync(RedisKey key, long count, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.RPop, Args.Key(key), count), ResultProcessors.Values, cancellationToken);

    public ValueTask<RedisValue[]> RangeAsync(RedisKey key, long start = 0, long stop = -1, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LRange, Args.Key(key), start, stop), ResultProcessors.Values, cancellationToken);

    public ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LLen, Args.Key(key)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<RedisValue> GetByIndexAsync(RedisKey key, long index, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LIndex, Args.Key(key), index), ResultProcessors.Value, cancellationToken);

    public async ValueTask SetByIndexAsync(RedisKey key, long index, RedisValue value, CancellationToken cancellationToken = default) =>
        await executor.ExecuteAsync(new RedisCommand(Commands.LSet, Args.Key(key), index, value), ResultProcessors.Ok, cancellationToken).ConfigureAwait(false);

    public ValueTask<long> InsertBeforeAsync(RedisKey key, RedisValue pivot, RedisValue value, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LInsert, Args.Key(key), "BEFORE", pivot, value), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> InsertAfterAsync(RedisKey key, RedisValue pivot, RedisValue value, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LInsert, Args.Key(key), "AFTER", pivot, value), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> RemoveAsync(RedisKey key, RedisValue value, long count = 0, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LRem, Args.Key(key), count, value), ResultProcessors.Integer, cancellationToken);

    public async ValueTask TrimAsync(RedisKey key, long start, long stop, CancellationToken cancellationToken = default) =>
        await executor.ExecuteAsync(new RedisCommand(Commands.LTrim, Args.Key(key), start, stop), ResultProcessors.Ok, cancellationToken).ConfigureAwait(false);

    public ValueTask<long?> PositionAsync(RedisKey key, RedisValue element, long? rank = null, long? maxLength = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LPos, PositionArgs(key, element, null, rank, maxLength)), ResultProcessors.NullableInteger, cancellationToken);

    public ValueTask<long[]> PositionsAsync(RedisKey key, RedisValue element, long count, long? rank = null, long? maxLength = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LPos, PositionArgs(key, element, count, rank, maxLength)), ResultProcessors.Integers, cancellationToken);

    public ValueTask<RedisValue> MoveAsync(RedisKey source, RedisKey destination, ListSide from, ListSide to, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.LMove, Args.Key(source), Args.Key(destination), Args.Side(from), Args.Side(to)), ResultProcessors.Value, cancellationToken);

    public async ValueTask<RedisValue> BlockingLeftPopAsync(RedisKey key, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        (await BlockingLeftPopAsync([key], timeout, cancellationToken).ConfigureAwait(false))?.Value ?? RedisValue.Null;

    public ValueTask<(RedisKey Key, RedisValue Value)?> BlockingLeftPopAsync(IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        BlockingPop(Commands.BLPop, keys, timeout, cancellationToken);

    public async ValueTask<RedisValue> BlockingRightPopAsync(RedisKey key, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        (await BlockingRightPopAsync([key], timeout, cancellationToken).ConfigureAwait(false))?.Value ?? RedisValue.Null;

    public ValueTask<(RedisKey Key, RedisValue Value)?> BlockingRightPopAsync(IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        BlockingPop(Commands.BRPop, keys, timeout, cancellationToken);

    public ValueTask<RedisValue> BlockingMoveAsync(RedisKey source, RedisKey destination, ListSide from, ListSide to, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(
            new RedisCommand(Commands.BLMove, Args.Key(source), Args.Key(destination), Args.Side(from), Args.Side(to), Args.Seconds(timeout)),
            ResultProcessors.Value,
            cancellationToken);

    private static RedisValue[] PositionArgs(RedisKey key, RedisValue element, long? count, long? rank, long? maxLength)
    {
        var args = new List<RedisValue> { Args.Key(key), element };
        if (rank is { } r)
        {
            args.Add("RANK");
            args.Add(r);
        }

        if (count is { } c)
        {
            args.Add("COUNT");
            args.Add(c);
        }

        if (maxLength is { } m)
        {
            args.Add("MAXLEN");
            args.Add(m);
        }

        return [.. args];
    }

    private ValueTask<long> Push(bool left, RedisKey key, IReadOnlyList<RedisValue> values, When when, CancellationToken cancellationToken)
    {
        var metadata = (left, when) switch
        {
            (true, When.Always) => Commands.LPush,
            (true, When.Exists) => Commands.LPushX,
            (false, When.Always) => Commands.RPush,
            (false, When.Exists) => Commands.RPushX,
            _ => throw new ArgumentOutOfRangeException(nameof(when), when, "List pushes support Always and Exists."),
        };
        return executor.ExecuteAsync(new RedisCommand(metadata, Args.KeyAndValues(key, values)), ResultProcessors.Integer, cancellationToken);
    }

    private ValueTask<(RedisKey Key, RedisValue Value)?> BlockingPop(CommandMetadata metadata, IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Args.NotEmpty(keys, nameof(keys));
        var args = Args.Keys(keys, suffix: 1);
        args[^1] = Args.Seconds(timeout);
        return executor.ExecuteAsync(new RedisCommand(metadata, args), ResultProcessors.KeyValue, cancellationToken);
    }
}
