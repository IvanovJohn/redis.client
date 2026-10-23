using Redis.Client.CommandGroups;
using Redis.Client.Protocol;

namespace Redis.Client;

/// <summary>Commands for string values.</summary>
public interface IStringCommands
{
    /// <summary>Gets a value (<c>GET</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The value, or <see cref="RedisValue.Null"/> if the key does not exist.</returns>
    ValueTask<RedisValue> GetAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Gets several values (<c>MGET</c>).</summary>
    /// <param name="keys">The keys.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>One value per key, <see cref="RedisValue.Null"/> for missing keys.</returns>
    ValueTask<RedisValue[]> GetAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default);

    /// <summary>Sets a value (<c>SET</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="expiry">Relative expiry (<c>EX</c>/<c>PX</c>); <see langword="null"/> removes any existing expiry unless <paramref name="keepTtl"/> is set.</param>
    /// <param name="when">Condition (<c>NX</c>/<c>XX</c>).</param>
    /// <param name="keepTtl">Keep the existing expiry (<c>KEEPTTL</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the value was set; <see langword="false"/> if the condition was not met.</returns>
    ValueTask<bool> SetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, bool keepTtl = false, CancellationToken cancellationToken = default);

    /// <summary>Sets a value that expires at a point in time (<c>SET ... EXAT</c>/<c>PXAT</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="expireAt">When the key is deleted.</param>
    /// <param name="when">Condition (<c>NX</c>/<c>XX</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the value was set; <see langword="false"/> if the condition was not met.</returns>
    ValueTask<bool> SetAsync(RedisKey key, RedisValue value, DateTimeOffset expireAt, When when = When.Always, CancellationToken cancellationToken = default);

    /// <summary>Sets a value and returns the previous one (<c>SET ... GET</c>). Combining with <see cref="When.NotExists"/> requires Redis 7.0+.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="expiry">Relative expiry.</param>
    /// <param name="when">Condition (<c>NX</c>/<c>XX</c>).</param>
    /// <param name="keepTtl">Keep the existing expiry (<c>KEEPTTL</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The previous value, or <see cref="RedisValue.Null"/>.</returns>
    ValueTask<RedisValue> SetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, bool keepTtl = false, CancellationToken cancellationToken = default);

    /// <summary>Sets several values atomically (<c>MSET</c>, or <c>MSETNX</c> with <see cref="When.NotExists"/>).</summary>
    /// <param name="values">Keys and values.</param>
    /// <param name="when"><see cref="When.NotExists"/> sets nothing unless none of the keys exists.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the values were set.</returns>
    ValueTask<bool> SetAsync(IReadOnlyList<(RedisKey Key, RedisValue Value)> values, When when = When.Always, CancellationToken cancellationToken = default);

    /// <summary>Gets a value and deletes the key (<c>GETDEL</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The value, or <see cref="RedisValue.Null"/>.</returns>
    ValueTask<RedisValue> GetDeleteAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Gets a value and changes its expiry (<c>GETEX</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="expiry">New relative expiry, or <see langword="null"/> to remove the expiry (<c>PERSIST</c>).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The value, or <see cref="RedisValue.Null"/>.</returns>
    ValueTask<RedisValue> GetAndExpireAsync(RedisKey key, TimeSpan? expiry, CancellationToken cancellationToken = default);

    /// <summary>Gets a value and sets an absolute expiry (<c>GETEX ... PXAT</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="expireAt">When the key is deleted.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The value, or <see cref="RedisValue.Null"/>.</returns>
    ValueTask<RedisValue> GetAndExpireAsync(RedisKey key, DateTimeOffset expireAt, CancellationToken cancellationToken = default);

    /// <summary>Gets a substring (<c>GETRANGE</c>); negative offsets count from the end.</summary>
    /// <param name="key">The key.</param>
    /// <param name="start">First byte offset.</param>
    /// <param name="end">Last byte offset (inclusive).</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The substring (empty if the key does not exist).</returns>
    ValueTask<RedisValue> GetRangeAsync(RedisKey key, long start, long end, CancellationToken cancellationToken = default);

    /// <summary>Overwrites part of a value (<c>SETRANGE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="offset">Byte offset.</param>
    /// <param name="value">The bytes to write.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length of the value after the change.</returns>
    ValueTask<long> SetRangeAsync(RedisKey key, long offset, RedisValue value, CancellationToken cancellationToken = default);

    /// <summary>Appends to a value (<c>APPEND</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value to append.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length of the value after the append.</returns>
    ValueTask<long> AppendAsync(RedisKey key, RedisValue value, CancellationToken cancellationToken = default);

    /// <summary>Returns the length of a value in bytes (<c>STRLEN</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length, or 0 if the key does not exist.</returns>
    ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Increments an integer value (<c>INCR</c>/<c>INCRBY</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The increment.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The new value.</returns>
    ValueTask<long> IncrementAsync(RedisKey key, long value = 1, CancellationToken cancellationToken = default);

    /// <summary>Increments a floating point value (<c>INCRBYFLOAT</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The increment.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The new value.</returns>
    ValueTask<double> IncrementAsync(RedisKey key, double value, CancellationToken cancellationToken = default);

    /// <summary>Decrements an integer value (<c>DECR</c>/<c>DECRBY</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The decrement.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The new value.</returns>
    ValueTask<long> DecrementAsync(RedisKey key, long value = 1, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="IStringCommands"/> over a command executor.</summary>
internal sealed class StringCommands(ICommandExecutor executor) : IStringCommands
{
    public ValueTask<RedisValue> GetAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Get, Args.Key(key)), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue[]> GetAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default)
    {
        Args.NotEmpty(keys, nameof(keys));
        return executor.ExecuteAsync(new RedisCommand(Commands.MGet, Args.Keys(keys)), ResultProcessors.Values, cancellationToken);
    }

    public ValueTask<bool> SetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, bool keepTtl = false, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Set, SetArgs(key, value, expiry, null, when, keepTtl, get: false)), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<bool> SetAsync(RedisKey key, RedisValue value, DateTimeOffset expireAt, When when = When.Always, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Set, SetArgs(key, value, null, expireAt, when, keepTtl: false, get: false)), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<RedisValue> SetAndGetAsync(RedisKey key, RedisValue value, TimeSpan? expiry = null, When when = When.Always, bool keepTtl = false, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Set, SetArgs(key, value, expiry, null, when, keepTtl, get: true)), ResultProcessors.Value, cancellationToken);

    public ValueTask<bool> SetAsync(IReadOnlyList<(RedisKey Key, RedisValue Value)> values, When when = When.Always, CancellationToken cancellationToken = default)
    {
        Args.NotEmpty(values, nameof(values));
        var metadata = when switch
        {
            When.Always => Commands.MSet,
            When.NotExists => Commands.MSetNx,
            _ => throw new ArgumentOutOfRangeException(nameof(when), when, "MSET supports Always and NotExists."),
        };
        var args = new RedisValue[values.Count * 2];
        for (var i = 0; i < values.Count; i++)
        {
            args[2 * i] = Args.Key(values[i].Key);
            args[(2 * i) + 1] = values[i].Value;
        }

        return executor.ExecuteAsync(new RedisCommand(metadata, args), ResultProcessors.Boolean, cancellationToken);
    }

    public ValueTask<RedisValue> GetDeleteAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.GetDel, Args.Key(key)), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue> GetAndExpireAsync(RedisKey key, TimeSpan? expiry, CancellationToken cancellationToken = default)
    {
        var args = new List<RedisValue> { Args.Key(key) };
        if (expiry is { } relative)
        {
            Args.Expiry(args, relative);
        }
        else
        {
            args.Add("PERSIST");
        }

        return executor.ExecuteAsync(new RedisCommand(Commands.GetEx, [.. args]), ResultProcessors.Value, cancellationToken);
    }

    public ValueTask<RedisValue> GetAndExpireAsync(RedisKey key, DateTimeOffset expireAt, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.GetEx, Args.Key(key), "PXAT", expireAt.ToUnixTimeMilliseconds()), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue> GetRangeAsync(RedisKey key, long start, long end, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.GetRange, Args.Key(key), start, end), ResultProcessors.Value, cancellationToken);

    public ValueTask<long> SetRangeAsync(RedisKey key, long offset, RedisValue value, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.SetRange, Args.Key(key), offset, value), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> AppendAsync(RedisKey key, RedisValue value, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Append, Args.Key(key), value), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.StrLen, Args.Key(key)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> IncrementAsync(RedisKey key, long value = 1, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(
            value == 1 ? new RedisCommand(Commands.Incr, Args.Key(key)) : new RedisCommand(Commands.IncrBy, Args.Key(key), value),
            ResultProcessors.Integer,
            cancellationToken);

    public ValueTask<double> IncrementAsync(RedisKey key, double value, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.IncrByFloat, Args.Key(key), value), ResultProcessors.Double, cancellationToken);

    public ValueTask<long> DecrementAsync(RedisKey key, long value = 1, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(
            value == 1 ? new RedisCommand(Commands.Decr, Args.Key(key)) : new RedisCommand(Commands.DecrBy, Args.Key(key), value),
            ResultProcessors.Integer,
            cancellationToken);

    private static RedisValue[] SetArgs(RedisKey key, RedisValue value, TimeSpan? expiry, DateTimeOffset? expireAt, When when, bool keepTtl, bool get)
    {
        if (keepTtl && (expiry is not null || expireAt is not null))
        {
            throw new ArgumentException("An expiry cannot be combined with keepTtl.", nameof(keepTtl));
        }

        var args = new List<RedisValue>(7) { Args.Key(key), value };
        if (expiry is { } relative)
        {
            Args.Expiry(args, relative);
        }
        else if (expireAt is { } absolute)
        {
            args.Add("PXAT");
            args.Add(absolute.ToUnixTimeMilliseconds());
        }
        else if (keepTtl)
        {
            args.Add("KEEPTTL");
        }

        switch (when)
        {
            case When.NotExists:
                args.Add("NX");
                break;
            case When.Exists:
                args.Add("XX");
                break;
            case When.Always:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(when), when, null);
        }

        if (get)
        {
            args.Add("GET");
        }

        return [.. args];
    }
}
