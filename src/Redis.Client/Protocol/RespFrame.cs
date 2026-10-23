using System.Buffers;
using System.Text;

namespace Redis.Client.Protocol;

/// <summary>
/// A view over one RESP frame inside a buffer. It does not copy data, so it is only valid until the buffer
/// is released; result processors convert it to owned values while it is valid.
/// </summary>
/// <remarks>
/// Frames are created only from data that <see cref="RespScanner"/> has already validated, so navigating them
/// never fails on malformed input.
/// </remarks>
internal readonly struct RespFrame
{
    private readonly ReadOnlySequence<byte> _format;
    private readonly long _attributeCount;

    private RespFrame(RespType type, ReadOnlySequence<byte> payload, long count, ReadOnlySequence<byte> attributes, ReadOnlySequence<byte> format = default, long attributeCount = 0)
    {
        Type = type;
        Payload = payload;
        Count = count;
        Attributes = attributes;
        _format = format;
        _attributeCount = attributeCount;
    }

    public RespType Type { get; }

    /// <summary>
    /// Scalar content (string bytes, number text; verbatim strings without their <c>fmt:</c> prefix), or the
    /// encoded children of an aggregate.
    /// </summary>
    public ReadOnlySequence<byte> Payload { get; }

    /// <summary>Number of elements of an aggregate (for maps: number of pairs).</summary>
    public long Count { get; }

    /// <summary>Encoded attribute map that preceded this frame, if any.</summary>
    public ReadOnlySequence<byte> Attributes { get; }

    public bool HasAttributes => !Attributes.IsEmpty;

    public bool IsNull => Type == RespType.Null;

    public bool IsError => Type is RespType.Error or RespType.BulkError;

    public bool IsAggregate => Type is RespType.Array or RespType.Set or RespType.Push or RespType.Map;

    /// <summary>Number of child frames: twice <see cref="Count"/> for maps.</summary>
    public long ChildCount => Type == RespType.Map ? Count * 2 : Count;

    /// <summary>Builds a frame over a complete, validated frame (attributes in front of it are captured).</summary>
    public static RespFrame Read(ReadOnlySequence<byte> validated)
    {
        var reader = new SequenceReader<byte>(validated);

        // A top-level aggregate's body is simply the rest of the validated frame; skipping all children just to
        // find where it ends would parse every element twice.
        if (reader.TryPeek(out var prefix) && (RespType)prefix is RespType.Array or RespType.Set or RespType.Push or RespType.Map)
        {
            var type = (RespType)prefix;
            reader.Advance(1);
            RespScanner.TryReadLine(ref reader, long.MaxValue, out var line);
            var count = RespScanner.ParseLength(line, type, allowNegative: true);
            return count < 0
                ? new RespFrame(RespType.Null, default, 0, default)
                : new RespFrame(type, validated.Slice(reader.Position), count, default);
        }

        return ReadElement(ref reader);
    }

    public ChildEnumerator GetChildren() => new(Payload, ChildCount);

    /// <summary>The attributes that preceded this frame as a map frame (check <see cref="HasAttributes"/> first).</summary>
    public RespFrame GetAttributeMap() => new(RespType.Map, Attributes, _attributeCount, default);

    /// <summary>Copies the children into an array (for processors that need random access).</summary>
    public RespFrame[] ToArray()
    {
        var result = new RespFrame[ChildCount];
        var i = 0;
        foreach (var child in GetChildren())
        {
            result[i++] = child;
        }

        return result;
    }

    public string GetString() => Encoding.UTF8.GetString(Payload);

    /// <summary>The format of a verbatim string, e.g. <c>txt</c> or <c>mkd</c>.</summary>
    public string? GetVerbatimFormat() => Type == RespType.VerbatimString ? Encoding.ASCII.GetString(_format) : null;

    /// <summary>Converts a scalar frame to an owned <see cref="RedisValue"/>.</summary>
    public RedisValue ToRedisValue() => Type switch
    {
        RespType.Null => RedisValue.Null,
        RespType.Integer => GetInt64(),
        RespType.Boolean => GetBoolean() ? 1L : 0L,
        RespType.Double => GetDouble(),
        _ when IsAggregate => throw Unexpected("a single value"),
        _ => Payload.ToArray(),
    };

    public long GetInt64()
    {
        switch (Type)
        {
            case RespType.Integer:
            case RespType.BulkString:
            case RespType.SimpleString:
            case RespType.BigNumber:
                Span<byte> scratch = stackalloc byte[64];
                var span = Payload.IsSingleSegment ? Payload.FirstSpan : CopySmall(scratch);
                return RedisValue.ParseInt64(span) ?? throw Unexpected("an integer");
            case RespType.Boolean:
                return GetBoolean() ? 1 : 0;
            case RespType.Double:
                var d = GetDouble();
                return Math.Floor(d) == d && double.IsFinite(d) ? (long)d : throw Unexpected("an integer");
            default:
                throw Unexpected("an integer");
        }
    }

    public double GetDouble()
    {
        switch (Type)
        {
            case RespType.Integer:
                return GetInt64();
            case RespType.Double:
            case RespType.BulkString:
            case RespType.SimpleString:
            case RespType.BigNumber:
                return ParseDouble(Payload) ?? throw Unexpected("a number");
            default:
                throw Unexpected("a number");
        }
    }

    public bool GetBoolean() => Type switch
    {
        RespType.Boolean => Payload.FirstSpan[0] == (byte)'t',
        RespType.Integer => GetInt64() != 0,
        RespType.SimpleString => Payload.FirstSpan.SequenceEqual("OK"u8) || GetInt64() != 0,
        _ => throw Unexpected("a boolean"),
    };

    public override string ToString() => IsAggregate ? $"{Type}[{Count}]" : $"{Type}: {(IsNull ? "nil" : GetString())}";

    internal static double? ParseDouble(ReadOnlySequence<byte> line)
    {
        if (line.IsSingleSegment)
        {
            return RedisValue.ParseDouble(line.FirstSpan);
        }

        if (line.Length > 512)
        {
            return null;
        }

        Span<byte> scratch = stackalloc byte[512];
        line.CopyTo(scratch);
        return RedisValue.ParseDouble(scratch[..(int)line.Length]);
    }

    /// <summary>Converts an error frame (<c>-</c> or <c>!</c>) to the exception reported to the caller.</summary>
    public RedisServerException ToServerException() => new(GetString());

    internal RedisException Unexpected(string expected) =>
        new($"Unexpected reply: expected {expected} but the server returned {Type}.");

    private static RespFrame ReadElement(ref SequenceReader<byte> reader)
    {
        reader.TryRead(out var prefix);
        var type = (RespType)prefix;
        RespScanner.TryReadLine(ref reader, long.MaxValue, out var line);
        switch (type)
        {
            case RespType.BulkString:
            case RespType.BulkError:
            case RespType.VerbatimString:
            {
                var length = RespScanner.ParseLength(line, type, allowNegative: true);
                if (length < 0)
                {
                    return new RespFrame(RespType.Null, default, 0, default);
                }

                var payload = reader.UnreadSequence.Slice(0, length);
                reader.Advance(length + 2);
                return type == RespType.VerbatimString
                    ? new RespFrame(type, payload.Slice(4), 0, default, payload.Slice(0, 3))
                    : new RespFrame(type, payload, 0, default);
            }

            case RespType.Array:
            case RespType.Set:
            case RespType.Push:
            case RespType.Map:
            case RespType.Attribute:
            {
                var count = RespScanner.ParseLength(line, type, allowNegative: true);
                if (count < 0)
                {
                    return new RespFrame(RespType.Null, default, 0, default);
                }

                var children = type is RespType.Map or RespType.Attribute ? count * 2 : count;
                var bodyStart = reader.Position;
                for (long i = 0; i < children; i++)
                {
                    ReadElement(ref reader);
                }

                var body = reader.Sequence.Slice(bodyStart, reader.Position);
                if (type == RespType.Attribute)
                {
                    var value = ReadElement(ref reader);
                    return new RespFrame(value.Type, value.Payload, value.Count, body, value._format, count);
                }

                return new RespFrame(type, body, count, default);
            }

            case RespType.Null:
                return new RespFrame(RespType.Null, default, 0, default);

            default:
                return new RespFrame(type, line, 0, default);
        }
    }

    private ReadOnlySpan<byte> CopySmall(Span<byte> scratch)
    {
        if (Payload.Length > scratch.Length)
        {
            throw Unexpected("an integer");
        }

        Payload.CopyTo(scratch);
        return scratch[..(int)Payload.Length];
    }

    /// <summary>Enumerates the children of an aggregate frame without allocating.</summary>
    internal ref struct ChildEnumerator
    {
        private SequenceReader<byte> _reader;
        private long _remaining;

        public ChildEnumerator(ReadOnlySequence<byte> body, long count)
        {
            _reader = new SequenceReader<byte>(body);
            _remaining = count;
            Current = default;
        }

        public RespFrame Current { get; private set; }

        public readonly ChildEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_remaining <= 0)
            {
                return false;
            }

            _remaining--;
            Current = ReadElement(ref _reader);
            return true;
        }
    }
}
