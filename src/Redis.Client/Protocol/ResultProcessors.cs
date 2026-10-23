namespace Redis.Client.Protocol;

/// <summary>
/// Reply converters shared by the typed commands. They accept both the RESP2 and the RESP3 shape of a reply, so a
/// typed method returns the same result whichever protocol was negotiated.
/// </summary>
internal static class ResultProcessors
{
    public static readonly Func<RespFrame, RedisValue> Value = static f => f.ToRedisValue();

    public static readonly Func<RespFrame, long> Integer = static f => f.GetInt64();

    public static readonly Func<RespFrame, long?> NullableInteger = static f => f.IsNull ? null : f.GetInt64();

    public static readonly Func<RespFrame, double> Double = static f => f.GetDouble();

    public static readonly Func<RespFrame, double?> NullableDouble = static f => f.IsNull ? null : f.GetDouble();

    /// <summary><c>1</c>/<c>0</c>, RESP3 booleans, <c>OK</c> (true) or <c>nil</c> (false).</summary>
    public static readonly Func<RespFrame, bool> Boolean = static f => !f.IsNull && f.GetBoolean();

    public static readonly Func<RespFrame, bool> Ok = static f => f.Type == RespType.SimpleString
        ? true
        : throw f.Unexpected("OK");

    public static readonly Func<RespFrame, RedisValue[]> Values = static f => ToValues(f);

    public static readonly Func<RespFrame, long[]> Integers = static f =>
    {
        if (f.IsNull)
        {
            return [];
        }

        var result = new long[f.ChildCount];
        var i = 0;
        foreach (var child in f.GetChildren())
        {
            result[i++] = child.GetInt64();
        }

        return result;
    };

    public static readonly Func<RespFrame, bool[]> Booleans = static f =>
    {
        var result = new bool[f.ChildCount];
        var i = 0;
        foreach (var child in f.GetChildren())
        {
            result[i++] = !child.IsNull && child.GetBoolean();
        }

        return result;
    };

    public static readonly Func<RespFrame, double?[]> NullableDoubles = static f =>
    {
        var result = new double?[f.ChildCount];
        var i = 0;
        foreach (var child in f.GetChildren())
        {
            result[i++] = child.IsNull ? null : child.GetDouble();
        }

        return result;
    };

    /// <summary>Field/value pairs from a RESP3 map, a flat RESP2 array or an array of two-element arrays.</summary>
    public static readonly Func<RespFrame, KeyValuePair<RedisValue, RedisValue>[]> Pairs = static f =>
    {
        var result = new List<KeyValuePair<RedisValue, RedisValue>>((int)Math.Min(f.ChildCount, 1024));
        foreach (var (first, second) in ReadPairs(f))
        {
            result.Add(new(first.ToRedisValue(), second.ToRedisValue()));
        }

        return [.. result];
    };

    /// <summary>Sorted set entries from a flat <c>member, score, ...</c> array or nested <c>[member, score]</c> pairs.</summary>
    public static readonly Func<RespFrame, SortedSetEntry[]> Entries = static f =>
    {
        var result = new List<SortedSetEntry>((int)Math.Min(f.ChildCount, 1024));
        foreach (var (member, score) in ReadPairs(f))
        {
            result.Add(new SortedSetEntry(member.ToRedisValue(), score.GetDouble()));
        }

        return [.. result];
    };

    public static readonly Func<RespFrame, SortedSetEntry?> NullableEntry = static f =>
    {
        var entries = Entries(f);
        return entries.Length == 0 ? null : entries[0];
    };

    public static readonly Func<RespFrame, RedisType> Type = static f => f.GetString() switch
    {
        "none" => RedisType.None,
        "string" => RedisType.String,
        "list" => RedisType.List,
        "set" => RedisType.Set,
        "zset" => RedisType.SortedSet,
        "hash" => RedisType.Hash,
        "stream" => RedisType.Stream,
        _ => RedisType.Unknown,
    };

    public static readonly Func<RespFrame, TimeToLive> TimeToLive = static f => f.GetInt64() switch
    {
        -2 => Client.TimeToLive.KeyMissing,
        -1 => Client.TimeToLive.NoExpiry,
        var ms => Client.TimeToLive.Expires(TimeSpan.FromMilliseconds(ms)),
    };

    /// <summary>A <c>[key, value]</c> reply of blocking pops; <see langword="null"/> on timeout.</summary>
    public static readonly Func<RespFrame, (RedisKey Key, RedisValue Value)?> KeyValue = static f =>
    {
        if (f.IsNull)
        {
            return null;
        }

        var items = f.ToArray();
        return ((byte[])items[0].ToRedisValue()!, items[1].ToRedisValue());
    };

    /// <summary>A <c>[key, member, score]</c> reply of <c>BZPOPMIN</c>/<c>BZPOPMAX</c>; <see langword="null"/> on timeout.</summary>
    public static readonly Func<RespFrame, (RedisKey Key, SortedSetEntry Entry)?> KeyEntry = static f =>
    {
        if (f.IsNull)
        {
            return null;
        }

        var items = f.ToArray();
        return ((byte[])items[0].ToRedisValue()!, new SortedSetEntry(items[1].ToRedisValue(), items[2].GetDouble()));
    };

    /// <summary>A <c>[cursor, [items]]</c> reply of the SCAN family.</summary>
    public static readonly Func<RespFrame, (RedisValue Cursor, RedisValue[] Items)> Scan = static f =>
    {
        var parts = f.ToArray();
        return (parts[0].ToRedisValue(), ToValues(parts[1]));
    };

    public static RedisValue[] ToValues(RespFrame frame)
    {
        if (frame.IsNull)
        {
            return [];
        }

        if (!frame.IsAggregate)
        {
            throw frame.Unexpected("an array");
        }

        var result = new RedisValue[frame.ChildCount];
        var i = 0;
        foreach (var child in frame.GetChildren())
        {
            result[i++] = child.ToRedisValue();
        }

        return result;
    }

    private static List<(RespFrame First, RespFrame Second)> ReadPairs(RespFrame frame)
    {
        var pairs = new List<(RespFrame, RespFrame)>();
        if (frame.IsNull)
        {
            return pairs;
        }

        if (!frame.IsAggregate)
        {
            throw frame.Unexpected("an array of pairs");
        }

        var children = frame.ToArray();
        if (children.Length > 0 && children[0].IsAggregate)
        {
            foreach (var pair in children)
            {
                var items = pair.ToArray();
                pairs.Add((items[0], items[1]));
            }
        }
        else
        {
            for (var i = 0; i + 1 < children.Length; i += 2)
            {
                pairs.Add((children[i], children[i + 1]));
            }
        }

        return pairs;
    }
}
