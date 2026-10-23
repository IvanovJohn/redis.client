using System.Runtime.CompilerServices;
using Redis.Client.CommandGroups;
using Redis.Client.Protocol;

namespace Redis.Client;

/// <summary>Commands for sorted sets.</summary>
public interface ISortedSetCommands
{
    /// <summary>Adds a member or updates its score (<c>ZADD</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member.</param>
    /// <param name="score">The score.</param>
    /// <param name="when">Conditions (<c>NX</c>, <c>XX</c>, <c>GT</c>, <c>LT</c>).</param>
    /// <param name="countChanged">Report changed members as well as added ones (<c>CH</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the member was added (or changed, with <paramref name="countChanged"/>).</returns>
    ValueTask<bool> AddAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, bool countChanged = false, CancellationToken cancellationToken = default);

    /// <summary>Adds members or updates their scores (<c>ZADD</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="entries">Members and scores.</param>
    /// <param name="when">Conditions (<c>NX</c>, <c>XX</c>, <c>GT</c>, <c>LT</c>).</param>
    /// <param name="countChanged">Count changed members as well as added ones (<c>CH</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of added (or changed) members.</returns>
    ValueTask<long> AddAsync(RedisKey key, IReadOnlyList<SortedSetEntry> entries, SortedSetWhen when = SortedSetWhen.Always, bool countChanged = false, CancellationToken cancellationToken = default);

    /// <summary>Increments the score of a member (<c>ZINCRBY</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member.</param>
    /// <param name="value">The increment.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The new score.</returns>
    ValueTask<double> IncrementAsync(RedisKey key, RedisValue member, double value, CancellationToken cancellationToken = default);

    /// <summary>Removes a member (<c>ZREM</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the member was removed.</returns>
    ValueTask<bool> RemoveAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default);

    /// <summary>Removes members (<c>ZREM</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="members">The members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of removed members.</returns>
    ValueTask<long> RemoveAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default);

    /// <summary>Returns the score of a member (<c>ZSCORE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The score, or <see langword="null"/> if the member does not exist.</returns>
    ValueTask<double?> ScoreAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default);

    /// <summary>Returns the scores of several members (<c>ZMSCORE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="members">The members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>One score per member, <see langword="null"/> for missing members.</returns>
    ValueTask<double?[]> ScoresAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default);

    /// <summary>Returns the rank of a member (<c>ZRANK</c>, or <c>ZREVRANK</c> for <see cref="Order.Descending"/>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="member">The member.</param>
    /// <param name="order">Rank by ascending or descending score.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The 0-based rank, or <see langword="null"/> if the member does not exist.</returns>
    ValueTask<long?> RankAsync(RedisKey key, RedisValue member, Order order = Order.Ascending, CancellationToken cancellationToken = default);

    /// <summary>Returns the number of members (<c>ZCARD</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of members.</returns>
    ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Counts members with a score in a range (<c>ZCOUNT</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of members in the range.</returns>
    ValueTask<long> CountAsync(RedisKey key, ScoreBound min, ScoreBound max, CancellationToken cancellationToken = default);

    /// <summary>Returns members by rank (<c>ZRANGE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="start">First rank; negative values count from the end.</param>
    /// <param name="stop">Last rank (inclusive).</param>
    /// <param name="order">Ascending or descending (<c>REV</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The members.</returns>
    ValueTask<RedisValue[]> RangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CancellationToken cancellationToken = default);

    /// <summary>Returns members and scores by rank (<c>ZRANGE ... WITHSCORES</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="start">First rank; negative values count from the end.</param>
    /// <param name="stop">Last rank (inclusive).</param>
    /// <param name="order">Ascending or descending (<c>REV</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The entries.</returns>
    ValueTask<SortedSetEntry[]> RangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CancellationToken cancellationToken = default);

    /// <summary>Returns members with a score in a range (<c>ZRANGE ... BYSCORE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="order">Ascending or descending (<c>REV</c>).</param>
    /// <param name="offset">Number of matches to skip (<c>LIMIT</c>).</param>
    /// <param name="count">Maximum number of matches (<c>LIMIT</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The members.</returns>
    ValueTask<RedisValue[]> RangeByScoreAsync(RedisKey key, ScoreBound min, ScoreBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default);

    /// <summary>Returns members and scores with a score in a range (<c>ZRANGE ... BYSCORE WITHSCORES</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="order">Ascending or descending (<c>REV</c>).</param>
    /// <param name="offset">Number of matches to skip (<c>LIMIT</c>).</param>
    /// <param name="count">Maximum number of matches (<c>LIMIT</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The entries.</returns>
    ValueTask<SortedSetEntry[]> RangeByScoreWithScoresAsync(RedisKey key, ScoreBound min, ScoreBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default);

    /// <summary>Returns members in a lexicographical range; all members must have the same score (<c>ZRANGE ... BYLEX</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="order">Ascending or descending (<c>REV</c>).</param>
    /// <param name="offset">Number of matches to skip (<c>LIMIT</c>).</param>
    /// <param name="count">Maximum number of matches (<c>LIMIT</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The members.</returns>
    ValueTask<RedisValue[]> RangeByLexAsync(RedisKey key, LexBound min, LexBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default);

    /// <summary>Stores members by rank into another key (<c>ZRANGESTORE</c>).</summary>
    /// <param name="destination">Where to store the result (overwritten).</param>
    /// <param name="source">The source sorted set.</param>
    /// <param name="start">First rank.</param>
    /// <param name="stop">Last rank (inclusive).</param>
    /// <param name="order">Ascending or descending (<c>REV</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of stored members.</returns>
    ValueTask<long> RangeByRankAndStoreAsync(RedisKey destination, RedisKey source, long start = 0, long stop = -1, Order order = Order.Ascending, CancellationToken cancellationToken = default);

    /// <summary>Stores members with a score in a range into another key (<c>ZRANGESTORE ... BYSCORE</c>).</summary>
    /// <param name="destination">Where to store the result (overwritten).</param>
    /// <param name="source">The source sorted set.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="order">Ascending or descending (<c>REV</c>).</param>
    /// <param name="offset">Number of matches to skip (<c>LIMIT</c>).</param>
    /// <param name="count">Maximum number of matches (<c>LIMIT</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of stored members.</returns>
    ValueTask<long> RangeByScoreAndStoreAsync(RedisKey destination, RedisKey source, ScoreBound min, ScoreBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default);

    /// <summary>Stores members in a lexicographical range into another key (<c>ZRANGESTORE ... BYLEX</c>).</summary>
    /// <param name="destination">Where to store the result (overwritten).</param>
    /// <param name="source">The source sorted set.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="order">Ascending or descending (<c>REV</c>).</param>
    /// <param name="offset">Number of matches to skip (<c>LIMIT</c>).</param>
    /// <param name="count">Maximum number of matches (<c>LIMIT</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of stored members.</returns>
    ValueTask<long> RangeByLexAndStoreAsync(RedisKey destination, RedisKey source, LexBound min, LexBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default);

    /// <summary>Removes members by rank (<c>ZREMRANGEBYRANK</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="start">First rank.</param>
    /// <param name="stop">Last rank (inclusive).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of removed members.</returns>
    ValueTask<long> RemoveRangeByRankAsync(RedisKey key, long start, long stop, CancellationToken cancellationToken = default);

    /// <summary>Removes members with a score in a range (<c>ZREMRANGEBYSCORE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of removed members.</returns>
    ValueTask<long> RemoveRangeByScoreAsync(RedisKey key, ScoreBound min, ScoreBound max, CancellationToken cancellationToken = default);

    /// <summary>Removes members in a lexicographical range (<c>ZREMRANGEBYLEX</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of removed members.</returns>
    ValueTask<long> RemoveRangeByLexAsync(RedisKey key, LexBound min, LexBound max, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns the member with the lowest score (<c>ZPOPMIN</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The entry, or <see langword="null"/> if the set is empty.</returns>
    ValueTask<SortedSetEntry?> PopMinAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns up to <paramref name="count"/> members with the lowest scores (<c>ZPOPMIN key count</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The maximum number of members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The entries, lowest first.</returns>
    ValueTask<SortedSetEntry[]> PopMinAsync(RedisKey key, long count, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns the member with the highest score (<c>ZPOPMAX</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The entry, or <see langword="null"/> if the set is empty.</returns>
    ValueTask<SortedSetEntry?> PopMaxAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes and returns up to <paramref name="count"/> members with the highest scores (<c>ZPOPMAX key count</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The maximum number of members.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The entries, highest first.</returns>
    ValueTask<SortedSetEntry[]> PopMaxAsync(RedisKey key, long count, CancellationToken cancellationToken = default);

    /// <summary>Waits for a member and removes the one with the lowest score (<c>BZPOPMIN</c>).</summary>
    /// <param name="keys">The keys, checked in order.</param>
    /// <param name="timeout">Maximum wait; <see cref="TimeSpan.Zero"/> waits forever.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The key and entry, or <see langword="null"/> if the timeout expired.</returns>
    ValueTask<(RedisKey Key, SortedSetEntry Entry)?> BlockingPopMinAsync(IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Waits for a member and removes the one with the highest score (<c>BZPOPMAX</c>).</summary>
    /// <param name="keys">The keys, checked in order.</param>
    /// <param name="timeout">Maximum wait; <see cref="TimeSpan.Zero"/> waits forever.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The key and entry, or <see langword="null"/> if the timeout expired.</returns>
    ValueTask<(RedisKey Key, SortedSetEntry Entry)?> BlockingPopMaxAsync(IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Computes a union, intersection or difference of sorted sets (<c>ZUNION</c>/<c>ZINTER</c>/<c>ZDIFF</c>).</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="keys">The sorted sets.</param>
    /// <param name="weights">Score multipliers, one per key (not for <see cref="SetOperation.Difference"/>).</param>
    /// <param name="aggregate">How scores are combined (not for <see cref="SetOperation.Difference"/>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The resulting members.</returns>
    ValueTask<RedisValue[]> CombineAsync(SetOperation operation, IReadOnlyList<RedisKey> keys, IReadOnlyList<double>? weights = null, Aggregate aggregate = Aggregate.Sum, CancellationToken cancellationToken = default);

    /// <summary>Computes a combination of sorted sets with scores (<c>... WITHSCORES</c>).</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="keys">The sorted sets.</param>
    /// <param name="weights">Score multipliers, one per key (not for <see cref="SetOperation.Difference"/>).</param>
    /// <param name="aggregate">How scores are combined (not for <see cref="SetOperation.Difference"/>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The resulting entries.</returns>
    ValueTask<SortedSetEntry[]> CombineWithScoresAsync(SetOperation operation, IReadOnlyList<RedisKey> keys, IReadOnlyList<double>? weights = null, Aggregate aggregate = Aggregate.Sum, CancellationToken cancellationToken = default);

    /// <summary>Stores a combination of sorted sets (<c>ZUNIONSTORE</c>/<c>ZINTERSTORE</c>/<c>ZDIFFSTORE</c>).</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="destination">Where to store the result (overwritten).</param>
    /// <param name="keys">The sorted sets.</param>
    /// <param name="weights">Score multipliers, one per key (not for <see cref="SetOperation.Difference"/>).</param>
    /// <param name="aggregate">How scores are combined (not for <see cref="SetOperation.Difference"/>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of members in the result.</returns>
    ValueTask<long> CombineAndStoreAsync(SetOperation operation, RedisKey destination, IReadOnlyList<RedisKey> keys, IReadOnlyList<double>? weights = null, Aggregate aggregate = Aggregate.Sum, CancellationToken cancellationToken = default);

    /// <summary>Iterates over the members of a sorted set (<c>ZSCAN</c>) without blocking the server.</summary>
    /// <param name="key">The key.</param>
    /// <param name="pattern">Glob-style pattern (<c>MATCH</c>).</param>
    /// <param name="pageSize">Hint for the amount of work per call (<c>COUNT</c>).</param>
    /// <param name="cancellationToken">Stops the iteration.</param>
    /// <returns>The entries.</returns>
    IAsyncEnumerable<SortedSetEntry> ScanAsync(RedisKey key, string? pattern = null, int? pageSize = null, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="ISortedSetCommands"/> over a command executor.</summary>
internal sealed class SortedSetCommands(ICommandExecutor executor) : ISortedSetCommands
{
    public ValueTask<bool> AddAsync(RedisKey key, RedisValue member, double score, SortedSetWhen when = SortedSetWhen.Always, bool countChanged = false, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZAdd, AddArgs(key, [new SortedSetEntry(member, score)], when, countChanged)), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> AddAsync(RedisKey key, IReadOnlyList<SortedSetEntry> entries, SortedSetWhen when = SortedSetWhen.Always, bool countChanged = false, CancellationToken cancellationToken = default)
    {
        Args.NotEmpty(entries, nameof(entries));
        return executor.ExecuteAsync(new RedisCommand(Commands.ZAdd, AddArgs(key, entries, when, countChanged)), ResultProcessors.Integer, cancellationToken);
    }

    public ValueTask<double> IncrementAsync(RedisKey key, RedisValue member, double value, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZIncrBy, Args.Key(key), value, member), ResultProcessors.Double, cancellationToken);

    public ValueTask<bool> RemoveAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRem, Args.Key(key), member), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> RemoveAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRem, Args.KeyAndValues(key, members)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<double?> ScoreAsync(RedisKey key, RedisValue member, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZScore, Args.Key(key), member), ResultProcessors.NullableDouble, cancellationToken);

    public ValueTask<double?[]> ScoresAsync(RedisKey key, IReadOnlyList<RedisValue> members, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZMScore, Args.KeyAndValues(key, members)), ResultProcessors.NullableDoubles, cancellationToken);

    public ValueTask<long?> RankAsync(RedisKey key, RedisValue member, Order order = Order.Ascending, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(
            new RedisCommand(order == Order.Ascending ? Commands.ZRank : Commands.ZRevRank, Args.Key(key), member),
            ResultProcessors.NullableInteger,
            cancellationToken);

    public ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZCard, Args.Key(key)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> CountAsync(RedisKey key, ScoreBound min, ScoreBound max, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZCount, Args.Key(key), Args.Score(min), Args.Score(max)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<RedisValue[]> RangeByRankAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRange, RangeArgs(null, key, start, stop, null, order, 0, null, withScores: false)), ResultProcessors.Values, cancellationToken);

    public ValueTask<SortedSetEntry[]> RangeByRankWithScoresAsync(RedisKey key, long start = 0, long stop = -1, Order order = Order.Ascending, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRange, RangeArgs(null, key, start, stop, null, order, 0, null, withScores: true)), ResultProcessors.Entries, cancellationToken);

    public ValueTask<RedisValue[]> RangeByScoreAsync(RedisKey key, ScoreBound min, ScoreBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRange, RangeArgs(null, key, Args.Score(min), Args.Score(max), "BYSCORE", order, offset, count, withScores: false)), ResultProcessors.Values, cancellationToken);

    public ValueTask<SortedSetEntry[]> RangeByScoreWithScoresAsync(RedisKey key, ScoreBound min, ScoreBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRange, RangeArgs(null, key, Args.Score(min), Args.Score(max), "BYSCORE", order, offset, count, withScores: true)), ResultProcessors.Entries, cancellationToken);

    public ValueTask<RedisValue[]> RangeByLexAsync(RedisKey key, LexBound min, LexBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRange, RangeArgs(null, key, min.ToRedisValue(), max.ToRedisValue(), "BYLEX", order, offset, count, withScores: false)), ResultProcessors.Values, cancellationToken);

    public ValueTask<long> RangeByRankAndStoreAsync(RedisKey destination, RedisKey source, long start = 0, long stop = -1, Order order = Order.Ascending, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRangeStore, RangeArgs(destination, source, start, stop, null, order, 0, null, withScores: false)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> RangeByScoreAndStoreAsync(RedisKey destination, RedisKey source, ScoreBound min, ScoreBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRangeStore, RangeArgs(destination, source, Args.Score(min), Args.Score(max), "BYSCORE", order, offset, count, withScores: false)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> RangeByLexAndStoreAsync(RedisKey destination, RedisKey source, LexBound min, LexBound max, Order order = Order.Ascending, long offset = 0, long? count = null, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRangeStore, RangeArgs(destination, source, min.ToRedisValue(), max.ToRedisValue(), "BYLEX", order, offset, count, withScores: false)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> RemoveRangeByRankAsync(RedisKey key, long start, long stop, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRemRangeByRank, Args.Key(key), start, stop), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> RemoveRangeByScoreAsync(RedisKey key, ScoreBound min, ScoreBound max, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRemRangeByScore, Args.Key(key), Args.Score(min), Args.Score(max)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> RemoveRangeByLexAsync(RedisKey key, LexBound min, LexBound max, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZRemRangeByLex, Args.Key(key), min.ToRedisValue(), max.ToRedisValue()), ResultProcessors.Integer, cancellationToken);

    public ValueTask<SortedSetEntry?> PopMinAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZPopMin, Args.Key(key)), ResultProcessors.NullableEntry, cancellationToken);

    public ValueTask<SortedSetEntry[]> PopMinAsync(RedisKey key, long count, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZPopMin, Args.Key(key), count), ResultProcessors.Entries, cancellationToken);

    public ValueTask<SortedSetEntry?> PopMaxAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZPopMax, Args.Key(key)), ResultProcessors.NullableEntry, cancellationToken);

    public ValueTask<SortedSetEntry[]> PopMaxAsync(RedisKey key, long count, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.ZPopMax, Args.Key(key), count), ResultProcessors.Entries, cancellationToken);

    public ValueTask<(RedisKey Key, SortedSetEntry Entry)?> BlockingPopMinAsync(IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        BlockingPop(Commands.BZPopMin, keys, timeout, cancellationToken);

    public ValueTask<(RedisKey Key, SortedSetEntry Entry)?> BlockingPopMaxAsync(IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        BlockingPop(Commands.BZPopMax, keys, timeout, cancellationToken);

    public ValueTask<RedisValue[]> CombineAsync(SetOperation operation, IReadOnlyList<RedisKey> keys, IReadOnlyList<double>? weights = null, Aggregate aggregate = Aggregate.Sum, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(CombineCommand(operation, store: false), CombineArgs(operation, null, keys, weights, aggregate, withScores: false)), ResultProcessors.Values, cancellationToken);

    public ValueTask<SortedSetEntry[]> CombineWithScoresAsync(SetOperation operation, IReadOnlyList<RedisKey> keys, IReadOnlyList<double>? weights = null, Aggregate aggregate = Aggregate.Sum, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(CombineCommand(operation, store: false), CombineArgs(operation, null, keys, weights, aggregate, withScores: true)), ResultProcessors.Entries, cancellationToken);

    public ValueTask<long> CombineAndStoreAsync(SetOperation operation, RedisKey destination, IReadOnlyList<RedisKey> keys, IReadOnlyList<double>? weights = null, Aggregate aggregate = Aggregate.Sum, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(CombineCommand(operation, store: true), CombineArgs(operation, destination, keys, weights, aggregate, withScores: false)), ResultProcessors.Integer, cancellationToken);

    public async IAsyncEnumerable<SortedSetEntry> ScanAsync(RedisKey key, string? pattern = null, int? pageSize = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RedisValue cursor = "0";
        do
        {
            var args = new List<RedisValue> { Args.Key(key), cursor };
            KeyCommands.ScanOptions(args, pattern, pageSize);
            var (next, items) = await executor.ExecuteAsync(new RedisCommand(Commands.ZScan, [.. args]), ResultProcessors.Scan, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i + 1 < items.Length; i += 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new SortedSetEntry(items[i], (double)items[i + 1]);
            }

            cursor = next;
        }
        while ((string?)cursor != "0");
    }

    private static RedisValue[] AddArgs(RedisKey key, IReadOnlyList<SortedSetEntry> entries, SortedSetWhen when, bool countChanged)
    {
        var exists = when.HasFlag(SortedSetWhen.Exists);
        var notExists = when.HasFlag(SortedSetWhen.NotExists);
        var greater = when.HasFlag(SortedSetWhen.GreaterThan);
        var less = when.HasFlag(SortedSetWhen.LessThan);
        if ((exists && notExists) || (greater && less) || (notExists && (greater || less)))
        {
            throw new ArgumentException($"Invalid combination of conditions: {when}. NotExists cannot be combined with other conditions, nor GreaterThan with LessThan.", nameof(when));
        }

        var args = new List<RedisValue>((entries.Count * 2) + 4) { Args.Key(key) };
        if (exists)
        {
            args.Add("XX");
        }

        if (notExists)
        {
            args.Add("NX");
        }

        if (greater)
        {
            args.Add("GT");
        }

        if (less)
        {
            args.Add("LT");
        }

        if (countChanged)
        {
            args.Add("CH");
        }

        foreach (var entry in entries)
        {
            args.Add(entry.Score);
            args.Add(entry.Member);
        }

        return [.. args];
    }

    private static RedisValue[] RangeArgs(RedisKey? destination, RedisKey source, RedisValue min, RedisValue max, string? by, Order order, long offset, long? count, bool withScores)
    {
        var args = new List<RedisValue>(10);
        if (destination is { } target)
        {
            args.Add(Args.Key(target));
        }

        args.Add(Args.Key(source));

        // With REV, BYSCORE and BYLEX ranges are given from the high end to the low end.
        var reversedBounds = order == Order.Descending && by is not null;
        args.Add(reversedBounds ? max : min);
        args.Add(reversedBounds ? min : max);
        if (by is not null)
        {
            args.Add(by);
        }

        if (order == Order.Descending)
        {
            args.Add("REV");
        }

        if (count is { } limit)
        {
            if (by is null)
            {
                throw new ArgumentException("LIMIT is only supported for score and lexicographical ranges.", nameof(count));
            }

            args.Add("LIMIT");
            args.Add(offset);
            args.Add(limit);
        }
        else if (offset != 0)
        {
            if (by is null)
            {
                throw new ArgumentException("An offset is only supported for score and lexicographical ranges.", nameof(offset));
            }

            args.Add("LIMIT");
            args.Add(offset);
            args.Add(-1L);
        }

        if (withScores)
        {
            args.Add("WITHSCORES");
        }

        return [.. args];
    }

    private static CommandMetadata CombineCommand(SetOperation operation, bool store) => (operation, store) switch
    {
        (SetOperation.Union, false) => Commands.ZUnion,
        (SetOperation.Intersect, false) => Commands.ZInter,
        (SetOperation.Difference, false) => Commands.ZDiff,
        (SetOperation.Union, true) => Commands.ZUnionStore,
        (SetOperation.Intersect, true) => Commands.ZInterStore,
        (SetOperation.Difference, true) => Commands.ZDiffStore,
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
    };

    private static RedisValue[] CombineArgs(SetOperation operation, RedisKey? destination, IReadOnlyList<RedisKey> keys, IReadOnlyList<double>? weights, Aggregate aggregate, bool withScores)
    {
        Args.NotEmpty(keys, nameof(keys));
        if (operation == SetOperation.Difference && (weights is not null || aggregate != Aggregate.Sum))
        {
            throw new ArgumentException("Weights and aggregation are not supported for the difference of sorted sets.", nameof(weights));
        }

        if (weights is not null && weights.Count != keys.Count)
        {
            throw new ArgumentException($"Expected one weight per key ({keys.Count}), got {weights.Count}.", nameof(weights));
        }

        var args = new List<RedisValue>(keys.Count + 8);
        if (destination is { } target)
        {
            args.Add(Args.Key(target));
        }

        args.Add(keys.Count);
        foreach (var key in keys)
        {
            args.Add(Args.Key(key));
        }

        if (weights is not null)
        {
            args.Add("WEIGHTS");
            foreach (var weight in weights)
            {
                args.Add(weight);
            }
        }

        if (aggregate != Aggregate.Sum)
        {
            args.Add("AGGREGATE");
            args.Add(aggregate == Aggregate.Min ? "MIN" : "MAX");
        }

        if (withScores)
        {
            args.Add("WITHSCORES");
        }

        return [.. args];
    }

    private ValueTask<(RedisKey Key, SortedSetEntry Entry)?> BlockingPop(CommandMetadata metadata, IReadOnlyList<RedisKey> keys, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Args.NotEmpty(keys, nameof(keys));
        var args = Args.Keys(keys, suffix: 1);
        args[^1] = Args.Seconds(timeout);
        return executor.ExecuteAsync(new RedisCommand(metadata, args), ResultProcessors.KeyEntry, cancellationToken);
    }
}
