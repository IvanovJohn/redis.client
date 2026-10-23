using System.Buffers;
using System.Buffers.Text;

namespace Redis.Client.Protocol;

/// <summary>
/// Validates that a buffer contains one complete RESP frame and finds its length.
/// </summary>
/// <remarks>
/// The scanner is resumable: when the frame is incomplete it remembers how many bytes have already been validated
/// and, on the next call with a longer buffer starting at the same frame, continues from there. A large reply that
/// arrives in many reads is therefore validated in linear time. Limits are enforced before any data is buffered:
/// an oversized length header fails immediately instead of waiting for (or allocating) the declared payload.
/// One instance serves one connection and is not thread-safe.
/// </remarks>
internal sealed class RespScanner
{
    private const int MaxNumberLineLength = 64;

    private readonly RespLimits _limits;
    private readonly long[] _remaining;
    private readonly bool[] _isAttribute;
    private int _depth;
    private long _offset;

    public RespScanner(RespLimits limits)
    {
        _limits = limits;
        _remaining = new long[limits.MaxNestingDepth + 1];
        _isAttribute = new bool[limits.MaxNestingDepth + 1];
    }

    /// <summary>Number of bytes already validated for the frame in progress.</summary>
    public long ValidatedBytes => _offset;

    /// <summary>Forgets any partially validated frame.</summary>
    public void Reset()
    {
        _depth = 0;
        _offset = 0;
    }

    /// <summary>
    /// Validates the frame at the start of <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">Unconsumed data; must start at the same frame as on previous incomplete calls.</param>
    /// <param name="frameLength">Total length of the complete frame.</param>
    /// <returns><see langword="true"/> if a complete frame is available; <see langword="false"/> if more data is needed.</returns>
    /// <exception cref="RedisProtocolException">The data is malformed or exceeds the limits.</exception>
    public bool TryScan(ReadOnlySequence<byte> buffer, out long frameLength)
    {
        var reader = new SequenceReader<byte>(buffer.Slice(_offset));
        var start = _offset;
        while (true)
        {
            if (!TryScanElement(ref reader, out var children, out var attribute))
            {
                frameLength = 0;
                return false;
            }

            _offset = start + reader.Consumed;

            if (children > 0)
            {
                if (_depth == _limits.MaxNestingDepth)
                {
                    throw new RedisProtocolException($"Reply nesting depth exceeds the limit of {_limits.MaxNestingDepth}.");
                }

                _depth++;
                _remaining[_depth] = children;
                _isAttribute[_depth] = attribute;
                continue;
            }

            if (attribute)
            {
                // An empty attribute is followed by the actual value; nothing has completed yet.
                continue;
            }

            if (CompleteElement())
            {
                frameLength = _offset;
                Reset();
                return true;
            }
        }
    }

    /// <summary>Accounts for one completed element; returns <see langword="true"/> when the whole frame is complete.</summary>
    private bool CompleteElement()
    {
        while (true)
        {
            if (_depth == 0)
            {
                return true;
            }

            if (--_remaining[_depth] > 0)
            {
                return false;
            }

            var wasAttribute = _isAttribute[_depth];
            _depth--;
            if (wasAttribute)
            {
                // The attribute map is done; the value it annotates follows and completes the parent instead.
                return false;
            }
        }
    }

    /// <summary>
    /// Scans one element header (and the payload of scalar elements).
    /// <paramref name="children"/> is the number of nested elements that follow for aggregates.
    /// </summary>
    private bool TryScanElement(ref SequenceReader<byte> reader, out long children, out bool attribute)
    {
        children = 0;
        attribute = false;
        if (!reader.TryRead(out var prefix))
        {
            return false;
        }

        var type = (RespType)prefix;
        switch (type)
        {
            case RespType.SimpleString:
            case RespType.Error:
            case RespType.BigNumber:
                return TryReadLine(ref reader, _limits.MaxBulkLength, out _);

            case RespType.Integer:
            {
                if (!TryReadLine(ref reader, MaxNumberLineLength, out var line))
                {
                    return false;
                }

                ParseLength(line, type, allowNegative: true);
                return true;
            }

            case RespType.Double:
            {
                if (!TryReadLine(ref reader, MaxNumberLineLength, out var line))
                {
                    return false;
                }

                if (RespFrame.ParseDouble(line) is null)
                {
                    throw Malformed(type, "invalid double");
                }

                return true;
            }

            case RespType.Boolean:
            {
                if (!TryReadLine(ref reader, MaxNumberLineLength, out var line))
                {
                    return false;
                }

                if (line.Length != 1 || (line.FirstSpan[0] != (byte)'t' && line.FirstSpan[0] != (byte)'f'))
                {
                    throw Malformed(type, "invalid boolean");
                }

                return true;
            }

            case RespType.Null:
            {
                if (!TryReadLine(ref reader, MaxNumberLineLength, out var line))
                {
                    return false;
                }

                if (line.Length != 0)
                {
                    throw Malformed(type, "null must be empty");
                }

                return true;
            }

            case RespType.BulkString:
            case RespType.BulkError:
            case RespType.VerbatimString:
            {
                if (!TryReadLine(ref reader, MaxNumberLineLength, out var line))
                {
                    return false;
                }

                var length = ParseLength(line, type, allowNegative: type == RespType.BulkString);
                if (length < 0)
                {
                    return true; // RESP2 null bulk string
                }

                if (length > _limits.MaxBulkLength)
                {
                    throw new RedisProtocolException($"Bulk reply of {length} bytes exceeds the limit of {_limits.MaxBulkLength} bytes.");
                }

                if (type == RespType.VerbatimString && length < 4)
                {
                    throw Malformed(type, "verbatim string is shorter than its format prefix");
                }

                if (reader.Remaining < length + 2)
                {
                    return false;
                }

                if (type == RespType.VerbatimString)
                {
                    var formatEnd = reader;
                    formatEnd.Advance(3);
                    if (!formatEnd.TryPeek(out var colon) || colon != (byte)':')
                    {
                        throw Malformed(type, "verbatim string has no format prefix");
                    }
                }

                reader.Advance(length);
                if (!reader.IsNext("\r\n"u8, advancePast: true))
                {
                    throw Malformed(type, "payload is not terminated by CRLF");
                }

                return true;
            }

            case RespType.Array:
            case RespType.Set:
            case RespType.Push:
            case RespType.Map:
            case RespType.Attribute:
            {
                if (!TryReadLine(ref reader, MaxNumberLineLength, out var line))
                {
                    return false;
                }

                var count = ParseLength(line, type, allowNegative: type == RespType.Array);
                if (count < 0)
                {
                    return true; // RESP2 null array
                }

                if (count > _limits.MaxBulkLength)
                {
                    throw new RedisProtocolException($"Aggregate reply of {count} elements exceeds the limit of {_limits.MaxBulkLength}.");
                }

                children = type is RespType.Map or RespType.Attribute ? count * 2 : count;
                attribute = type == RespType.Attribute;
                return true;
            }

            default:
                throw new RedisProtocolException($"Unexpected RESP type byte 0x{prefix:X2}.");
        }
    }

    internal static bool TryReadLine(ref SequenceReader<byte> reader, long maxLength, out ReadOnlySequence<byte> line)
    {
        // Fast path: the whole line is in the current segment (by far the common case).
        var span = reader.UnreadSpan;
        var cr = span.IndexOf((byte)'\r');
        if (cr >= 0 && cr + 1 < span.Length && span[cr + 1] == (byte)'\n')
        {
            if (cr > maxLength)
            {
                throw new RedisProtocolException($"RESP line of {cr} bytes exceeds the limit of {maxLength} bytes.");
            }

            line = reader.UnreadSequence.Slice(0, cr);
            reader.Advance(cr + 2);
            return true;
        }

        if (reader.TryReadTo(out line, "\r\n"u8, advancePastDelimiter: true))
        {
            if (line.Length > maxLength)
            {
                throw new RedisProtocolException($"RESP line of {line.Length} bytes exceeds the limit of {maxLength} bytes.");
            }

            return true;
        }

        if (reader.Remaining > maxLength + 1)
        {
            throw new RedisProtocolException($"RESP line is not terminated within {maxLength} bytes.");
        }

        return false;
    }

    internal static long ParseLength(ReadOnlySequence<byte> line, RespType type, bool allowNegative)
    {
        Span<byte> scratch = stackalloc byte[MaxNumberLineLength];
        var span = line.IsSingleSegment ? line.FirstSpan : Copy(line, scratch);
        if (!Utf8Parser.TryParse(span, out long value, out var consumed) || consumed != span.Length || span.Length == 0)
        {
            throw Malformed(type, "invalid number");
        }

        if (value < 0 && !(allowNegative && (type == RespType.Integer || value == -1)))
        {
            throw Malformed(type, "negative length");
        }

        return value;
    }

    internal static ReadOnlySpan<byte> Copy(ReadOnlySequence<byte> line, Span<byte> scratch)
    {
        if (line.Length > scratch.Length)
        {
            throw new RedisProtocolException("RESP number is too long.");
        }

        line.CopyTo(scratch);
        return scratch[..(int)line.Length];
    }

    private static RedisProtocolException Malformed(RespType type, string reason) =>
        new($"Malformed RESP {type} frame: {reason}.");
}
