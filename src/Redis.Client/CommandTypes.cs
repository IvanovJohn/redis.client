using System.Globalization;

namespace Redis.Client;

/// <summary>Condition for writing a key, field or list.</summary>
public enum When
{
    /// <summary>Write unconditionally.</summary>
    Always = 0,

    /// <summary>Write only if it does not exist yet (<c>NX</c>).</summary>
    NotExists = 1,

    /// <summary>Write only if it already exists (<c>XX</c>).</summary>
    Exists = 2,
}

/// <summary>Type of the value stored at a key, as returned by <c>TYPE</c>.</summary>
public enum RedisType
{
    /// <summary>The key does not exist.</summary>
    None = 0,

    /// <summary>A string.</summary>
    String,

    /// <summary>A list.</summary>
    List,

    /// <summary>A set.</summary>
    Set,

    /// <summary>A sorted set.</summary>
    SortedSet,

    /// <summary>A hash.</summary>
    Hash,

    /// <summary>A stream.</summary>
    Stream,

    /// <summary>A type this client does not know (e.g. from a module).</summary>
    Unknown,
}

/// <summary>End of a list.</summary>
public enum ListSide
{
    /// <summary>The head (index 0).</summary>
    Left = 0,

    /// <summary>The tail (index -1).</summary>
    Right = 1,
}

/// <summary>Set algebra operation.</summary>
public enum SetOperation
{
    /// <summary>Members present in any of the sets.</summary>
    Union = 0,

    /// <summary>Members present in all of the sets.</summary>
    Intersect = 1,

    /// <summary>Members of the first set that are not in any of the others.</summary>
    Difference = 2,
}

/// <summary>Sort order of sorted set ranges.</summary>
public enum Order
{
    /// <summary>Lowest score (or rank) first.</summary>
    Ascending = 0,

    /// <summary>Highest score (or rank) first (<c>REV</c>).</summary>
    Descending = 1,
}

/// <summary>How scores are combined by sorted set union and intersection.</summary>
public enum Aggregate
{
    /// <summary>Sum of the (weighted) scores.</summary>
    Sum = 0,

    /// <summary>Lowest (weighted) score.</summary>
    Min = 1,

    /// <summary>Highest (weighted) score.</summary>
    Max = 2,
}

/// <summary>Conditions of <c>ZADD</c>. <see cref="Exists"/> can be combined with <see cref="GreaterThan"/> or <see cref="LessThan"/>.</summary>
[Flags]
public enum SortedSetWhen
{
    /// <summary>Add new members and update existing ones.</summary>
    Always = 0,

    /// <summary>Only update existing members (<c>XX</c>).</summary>
    Exists = 1,

    /// <summary>Only add new members (<c>NX</c>).</summary>
    NotExists = 2,

    /// <summary>Only update a score if the new one is greater (<c>GT</c>); new members are still added.</summary>
    GreaterThan = 4,

    /// <summary>Only update a score if the new one is lower (<c>LT</c>); new members are still added.</summary>
    LessThan = 8,
}

/// <summary>State of a key's time to live.</summary>
public enum TimeToLiveKind
{
    /// <summary>The key expires after <see cref="TimeToLive.Remaining"/>.</summary>
    Expires = 0,

    /// <summary>The key exists and has no expiry.</summary>
    NoExpiry = 1,

    /// <summary>The key does not exist.</summary>
    KeyMissing = 2,
}

/// <summary>Remaining time to live of a key (<c>PTTL</c>).</summary>
public readonly record struct TimeToLive
{
    private TimeToLive(TimeToLiveKind kind, TimeSpan remaining)
    {
        Kind = kind;
        Remaining = remaining;
    }

    /// <summary>Gets the result for a key without expiry.</summary>
    public static TimeToLive NoExpiry { get; } = new(TimeToLiveKind.NoExpiry, TimeSpan.Zero);

    /// <summary>Gets the result for a missing key.</summary>
    public static TimeToLive KeyMissing { get; } = new(TimeToLiveKind.KeyMissing, TimeSpan.Zero);

    /// <summary>Gets whether the key expires, has no expiry or does not exist.</summary>
    public TimeToLiveKind Kind { get; }

    /// <summary>Gets the remaining time when <see cref="Kind"/> is <see cref="TimeToLiveKind.Expires"/>; otherwise zero.</summary>
    public TimeSpan Remaining { get; }

    /// <summary>Creates a result for a key that expires after <paramref name="remaining"/>.</summary>
    /// <param name="remaining">The remaining time.</param>
    /// <returns>The result.</returns>
    public static TimeToLive Expires(TimeSpan remaining) => new(TimeToLiveKind.Expires, remaining);

    /// <summary>Returns a description such as <c>Expires in 00:00:10</c>.</summary>
    /// <returns>The description.</returns>
    public override string ToString() => Kind == TimeToLiveKind.Expires ? $"Expires in {Remaining}" : Kind.ToString();
}

/// <summary>A member of a sorted set with its score.</summary>
/// <param name="Member">The member.</param>
/// <param name="Score">The score.</param>
public readonly record struct SortedSetEntry(RedisValue Member, double Score);

/// <summary>A boundary of a score range: inclusive (default), exclusive or infinite.</summary>
public readonly struct ScoreBound : IEquatable<ScoreBound>
{
    private ScoreBound(double value, bool exclusive)
    {
        Value = value;
        IsExclusive = exclusive;
    }

    /// <summary>Gets the lowest possible bound (<c>-inf</c>).</summary>
    public static ScoreBound NegativeInfinity { get; } = new(double.NegativeInfinity, false);

    /// <summary>Gets the highest possible bound (<c>+inf</c>).</summary>
    public static ScoreBound PositiveInfinity { get; } = new(double.PositiveInfinity, false);

    /// <summary>Gets the score.</summary>
    public double Value { get; }

    /// <summary>Gets a value indicating whether the score itself is excluded from the range.</summary>
    public bool IsExclusive { get; }

    /// <summary>Creates an inclusive bound.</summary>
    /// <param name="value">The score.</param>
    public static implicit operator ScoreBound(double value) => Inclusive(value);

    /// <summary>Compares two bounds.</summary>
    /// <param name="left">The first bound.</param>
    /// <param name="right">The second bound.</param>
    public static bool operator ==(ScoreBound left, ScoreBound right) => left.Equals(right);

    /// <summary>Compares two bounds.</summary>
    /// <param name="left">The first bound.</param>
    /// <param name="right">The second bound.</param>
    public static bool operator !=(ScoreBound left, ScoreBound right) => !left.Equals(right);

    /// <summary>Creates a bound that includes <paramref name="value"/>.</summary>
    /// <param name="value">The score.</param>
    /// <returns>The bound.</returns>
    public static ScoreBound Inclusive(double value) => new(value, false);

    /// <summary>Creates a bound that excludes <paramref name="value"/> (<c>(value</c>).</summary>
    /// <param name="value">The score.</param>
    /// <returns>The bound.</returns>
    public static ScoreBound Exclusive(double value) => new(value, true);

    /// <inheritdoc />
    public bool Equals(ScoreBound other) => Value.Equals(other.Value) && IsExclusive == other.IsExclusive;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ScoreBound other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Value, IsExclusive);

    /// <summary>Returns the Redis syntax of the bound, e.g. <c>(1.5</c> or <c>+inf</c>.</summary>
    /// <returns>The bound as sent to Redis.</returns>
    public override string ToString()
    {
        var text = double.IsPositiveInfinity(Value) ? "+inf"
            : double.IsNegativeInfinity(Value) ? "-inf"
            : Value.ToString("R", CultureInfo.InvariantCulture);
        return IsExclusive ? "(" + text : text;
    }
}

/// <summary>A boundary of a lexicographical range: inclusive, exclusive, or the open ends <c>-</c> and <c>+</c>.</summary>
public readonly struct LexBound : IEquatable<LexBound>
{
    private readonly byte _kind; // 0 inclusive, 1 exclusive, 2 min, 3 max

    private LexBound(RedisValue value, byte kind)
    {
        Value = value;
        _kind = kind;
    }

    /// <summary>Gets the bound below every member (<c>-</c>).</summary>
    public static LexBound Min { get; } = new(RedisValue.Null, 2);

    /// <summary>Gets the bound above every member (<c>+</c>).</summary>
    public static LexBound Max { get; } = new(RedisValue.Null, 3);

    /// <summary>Gets the member value of the bound (null for <see cref="Min"/> and <see cref="Max"/>).</summary>
    public RedisValue Value { get; }

    /// <summary>Compares two bounds.</summary>
    /// <param name="left">The first bound.</param>
    /// <param name="right">The second bound.</param>
    public static bool operator ==(LexBound left, LexBound right) => left.Equals(right);

    /// <summary>Compares two bounds.</summary>
    /// <param name="left">The first bound.</param>
    /// <param name="right">The second bound.</param>
    public static bool operator !=(LexBound left, LexBound right) => !left.Equals(right);

    /// <summary>Creates a bound that includes <paramref name="value"/> (<c>[value</c>).</summary>
    /// <param name="value">The member.</param>
    /// <returns>The bound.</returns>
    public static LexBound Inclusive(RedisValue value) => new(value, 0);

    /// <summary>Creates a bound that excludes <paramref name="value"/> (<c>(value</c>).</summary>
    /// <param name="value">The member.</param>
    /// <returns>The bound.</returns>
    public static LexBound Exclusive(RedisValue value) => new(value, 1);

    /// <inheritdoc />
    public bool Equals(LexBound other) => _kind == other._kind && Value.Equals(other.Value);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LexBound other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_kind, Value);

    /// <summary>Returns the Redis syntax of the bound.</summary>
    /// <returns>The bound as sent to Redis.</returns>
    public override string ToString() => (string?)ToRedisValue() ?? string.Empty;

    internal RedisValue ToRedisValue() => _kind switch
    {
        2 => "-",
        3 => "+",
        _ => Prefix(_kind == 0 ? (byte)'[' : (byte)'('),
    };

    private RedisValue Prefix(byte prefix)
    {
        var bytes = new byte[Value.GetByteCount() + 1];
        bytes[0] = prefix;
        Value.CopyTo(bytes.AsSpan(1));
        return bytes;
    }
}
