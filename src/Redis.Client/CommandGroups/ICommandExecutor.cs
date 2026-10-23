using Redis.Client.Protocol;

namespace Redis.Client.CommandGroups;

/// <summary>
/// Runs commands for the typed command groups. The client sends them right away; a transaction collects them and
/// completes their results after <c>EXEC</c>.
/// </summary>
internal interface ICommandExecutor
{
    ValueTask<T> ExecuteAsync<T>(RedisCommand command, Func<RespFrame, T> processor, CancellationToken cancellationToken);
}

/// <summary>Helpers to build argument arrays.</summary>
internal static class Args
{
    public static RedisValue Key(RedisKey key)
    {
        if (key.IsNull)
        {
            throw new ArgumentException("The key must not be null.", nameof(key));
        }

        return key;
    }

    public static RedisValue[] Keys(IReadOnlyList<RedisKey> keys, int prefix = 0, int suffix = 0)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var result = new RedisValue[prefix + keys.Count + suffix];
        for (var i = 0; i < keys.Count; i++)
        {
            result[prefix + i] = Key(keys[i]);
        }

        return result;
    }

    public static RedisValue[] KeyAndValues(RedisKey key, IReadOnlyList<RedisValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        var result = new RedisValue[values.Count + 1];
        result[0] = Key(key);
        for (var i = 0; i < values.Count; i++)
        {
            result[i + 1] = values[i];
        }

        return result;
    }

    public static void NotEmpty<T>(IReadOnlyList<T> items, string name)
    {
        ArgumentNullException.ThrowIfNull(items, name);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", name);
        }
    }

    /// <summary>Formats a blocking timeout in seconds; zero blocks forever.</summary>
    public static RedisValue Seconds(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must not be negative.");
        }

        return timeout == Timeout.InfiniteTimeSpan ? 0L : timeout.TotalSeconds;
    }

    /// <summary>Adds an expiry: <c>EX</c> for whole seconds, otherwise <c>PX</c>.</summary>
    public static void Expiry(List<RedisValue> args, TimeSpan expiry)
    {
        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiry), "The expiry must be positive.");
        }

        if (expiry.Ticks % TimeSpan.TicksPerSecond == 0)
        {
            args.Add("EX");
            args.Add((long)expiry.TotalSeconds);
        }
        else
        {
            args.Add("PX");
            args.Add((long)Math.Ceiling(expiry.TotalMilliseconds));
        }
    }

    public static RedisValue Side(ListSide side) => side == ListSide.Left ? "LEFT" : "RIGHT";

    public static RedisValue Score(ScoreBound bound) => bound.ToString();
}
