using System.Buffers;
using System.Globalization;
using System.Text;

namespace Redis.Client;

/// <summary>
/// A value sent to or received from Redis: a binary-safe string, an integer, a floating point number or
/// <see cref="Null"/> (the Redis <c>nil</c> reply).
/// </summary>
/// <remarks>
/// Values are created implicitly from <see cref="string"/> (UTF-8), byte arrays, <see cref="ReadOnlyMemory{T}"/>,
/// <see cref="long"/> and <see cref="double"/>, and converted back with explicit casts. Redis stores everything
/// as bytes, so two values are equal when their byte representations are equal: <c>42</c> equals <c>"42"</c>.
/// </remarks>
public readonly struct RedisValue : IEquatable<RedisValue>
{
    private readonly string? _string;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly long _bits;
    private readonly Kind _kind;

    private RedisValue(Kind kind, string? text, ReadOnlyMemory<byte> bytes, long bits)
    {
        _kind = kind;
        _string = text;
        _bytes = bytes;
        _bits = bits;
    }

    private enum Kind : byte
    {
        Null = 0,
        String,
        Bytes,
        Int64,
        Double,
    }

    /// <summary>Gets the <c>nil</c> value.</summary>
    public static RedisValue Null => default;

    /// <summary>Gets an empty (zero-length) value.</summary>
    public static RedisValue EmptyString { get; } = new(Kind.String, string.Empty, default, 0);

    /// <summary>Gets a value indicating whether this is <see cref="Null"/>.</summary>
    public bool IsNull => _kind == Kind.Null;

    /// <summary>Gets a value indicating whether this is <see cref="Null"/> or a zero-length value.</summary>
    public bool IsNullOrEmpty => _kind switch
    {
        Kind.Null => true,
        Kind.String => _string!.Length == 0,
        Kind.Bytes => _bytes.IsEmpty,
        _ => false,
    };

    /// <summary>Creates a value from a string encoded as UTF-8; <see langword="null"/> becomes <see cref="Null"/>.</summary>
    /// <param name="value">The string.</param>
    public static implicit operator RedisValue(string? value) =>
        value is null ? Null : new RedisValue(Kind.String, value, default, 0);

    /// <summary>Creates a value from bytes without copying them; <see langword="null"/> becomes <see cref="Null"/>.</summary>
    /// <param name="value">The bytes.</param>
    public static implicit operator RedisValue(byte[]? value) =>
        value is null ? Null : new RedisValue(Kind.Bytes, null, value, 0);

    /// <summary>Creates a value from bytes without copying them.</summary>
    /// <param name="value">The bytes.</param>
    public static implicit operator RedisValue(ReadOnlyMemory<byte> value) => new(Kind.Bytes, null, value, 0);

    /// <summary>Creates an integer value.</summary>
    /// <param name="value">The integer.</param>
    public static implicit operator RedisValue(long value) => new(Kind.Int64, null, default, value);

    /// <summary>Creates a floating point value. Infinities are sent as <c>+inf</c> and <c>-inf</c>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator RedisValue(double value) => new(Kind.Double, null, default, BitConverter.DoubleToInt64Bits(value));

    /// <summary>Returns the value as a string (bytes are decoded as UTF-8); <see cref="Null"/> becomes <see langword="null"/>.</summary>
    /// <param name="value">The value.</param>
    public static explicit operator string?(RedisValue value) => value._kind switch
    {
        Kind.Null => null,
        Kind.String => value._string,
        Kind.Bytes => Encoding.UTF8.GetString(value._bytes.Span),
        Kind.Int64 => value._bits.ToString(CultureInfo.InvariantCulture),
        _ => FormatDouble(value.AsDouble()),
    };

    /// <summary>Returns a copy of the value bytes; <see cref="Null"/> becomes <see langword="null"/>.</summary>
    /// <param name="value">The value.</param>
    public static explicit operator byte[]?(RedisValue value)
    {
        if (value.IsNull)
        {
            return null;
        }

        if (value._kind == Kind.Bytes)
        {
            return value._bytes.ToArray();
        }

        var result = new byte[value.GetByteCount()];
        value.CopyTo(result);
        return result;
    }

    /// <summary>Parses the value as a 64-bit integer.</summary>
    /// <param name="value">The value.</param>
    /// <exception cref="InvalidCastException">The value is <see cref="Null"/> or not an integer.</exception>
    public static explicit operator long(RedisValue value) =>
        value.TryParse(out long result) ? result : throw CastError(value, "Int64");

    /// <summary>Parses the value as a 64-bit integer; <see cref="Null"/> becomes <see langword="null"/>.</summary>
    /// <param name="value">The value.</param>
    /// <exception cref="InvalidCastException">The value is not an integer.</exception>
    public static explicit operator long?(RedisValue value) => value.IsNull ? null : (long)value;

    /// <summary>Parses the value as a 32-bit integer.</summary>
    /// <param name="value">The value.</param>
    /// <exception cref="InvalidCastException">The value is <see cref="Null"/>, not an integer or out of range.</exception>
    public static explicit operator int(RedisValue value)
    {
        var result = (long)value;
        return result is >= int.MinValue and <= int.MaxValue ? (int)result : throw CastError(value, "Int32");
    }

    /// <summary>Parses the value as a floating point number (<c>inf</c>, <c>-inf</c> and <c>nan</c> are accepted).</summary>
    /// <param name="value">The value.</param>
    /// <exception cref="InvalidCastException">The value is <see cref="Null"/> or not a number.</exception>
    public static explicit operator double(RedisValue value) =>
        value.TryParse(out double result) ? result : throw CastError(value, "Double");

    /// <summary>Parses the value as a floating point number; <see cref="Null"/> becomes <see langword="null"/>.</summary>
    /// <param name="value">The value.</param>
    /// <exception cref="InvalidCastException">The value is not a number.</exception>
    public static explicit operator double?(RedisValue value) => value.IsNull ? null : (double)value;

    /// <summary>Compares two values by their byte representation.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator ==(RedisValue left, RedisValue right) => left.Equals(right);

    /// <summary>Compares two values by their byte representation.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    public static bool operator !=(RedisValue left, RedisValue right) => !left.Equals(right);

    /// <summary>Tries to parse the value as a 64-bit integer.</summary>
    /// <param name="result">The parsed integer.</param>
    /// <returns><see langword="true"/> if the value is an integer.</returns>
    public bool TryParse(out long result)
    {
        switch (_kind)
        {
            case Kind.Int64:
                result = _bits;
                return true;
            case Kind.Double:
                var d = AsDouble();
                if (double.IsFinite(d) && Math.Floor(d) == d && d is >= long.MinValue and < 9.2233720368547758E+18)
                {
                    result = (long)d;
                    return true;
                }

                result = 0;
                return false;
            case Kind.Null:
                result = 0;
                return false;
            default:
                var parsed = ByteHelpers.WithBytes(this, static span => ParseInt64(span));
                result = parsed ?? 0;
                return parsed.HasValue;
        }
    }

    /// <summary>Tries to parse the value as a floating point number.</summary>
    /// <param name="result">The parsed number.</param>
    /// <returns><see langword="true"/> if the value is a number.</returns>
    public bool TryParse(out double result)
    {
        switch (_kind)
        {
            case Kind.Double:
                result = AsDouble();
                return true;
            case Kind.Int64:
                result = _bits;
                return true;
            case Kind.Null:
                result = 0;
                return false;
            default:
                var parsed = ByteHelpers.WithBytes(this, static span => ParseDouble(span));
                result = parsed ?? 0;
                return parsed.HasValue;
        }
    }

    /// <inheritdoc />
    public bool Equals(RedisValue other)
    {
        if (IsNull || other.IsNull)
        {
            return IsNull && other.IsNull;
        }

        if (_kind == other._kind)
        {
            switch (_kind)
            {
                case Kind.String:
                    return string.Equals(_string, other._string, StringComparison.Ordinal);
                case Kind.Bytes:
                    return _bytes.Span.SequenceEqual(other._bytes.Span);
                case Kind.Int64:
                    return _bits == other._bits;
            }
        }

        var otherBytes = (byte[])other!;
        return ByteHelpers.WithBytes(this, span => span.SequenceEqual(otherBytes));
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RedisValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => IsNull ? 0 : ByteHelpers.WithBytes(this, static span => ByteHelpers.Hash(span));

    /// <summary>Returns the value as text for diagnostics; <see cref="Null"/> is shown as <c>(nil)</c>.</summary>
    /// <returns>The value text.</returns>
    public override string ToString() => IsNull ? "(nil)" : (string)this!;

    /// <summary>Number of bytes the value occupies on the wire.</summary>
    internal int GetByteCount()
    {
        switch (_kind)
        {
            case Kind.Null:
                return 0;
            case Kind.String:
                return Encoding.UTF8.GetByteCount(_string!);
            case Kind.Bytes:
                return _bytes.Length;
            default:
                Span<byte> scratch = stackalloc byte[32];
                return FormatNumber(scratch);
        }
    }

    /// <summary>Writes the value bytes into <paramref name="destination"/>, which must be large enough.</summary>
    internal int CopyTo(Span<byte> destination)
    {
        switch (_kind)
        {
            case Kind.Null:
                return 0;
            case Kind.String:
                return Encoding.UTF8.GetBytes(_string!, destination);
            case Kind.Bytes:
                _bytes.Span.CopyTo(destination);
                return _bytes.Length;
            default:
                return FormatNumber(destination);
        }
    }

    /// <summary>Writes exactly <paramref name="length"/> (= <see cref="GetByteCount"/>) bytes to <paramref name="writer"/>.</summary>
    internal void WriteTo(IBufferWriter<byte> writer, int length)
    {
        switch (_kind)
        {
            case Kind.Null:
                return;
            case Kind.Bytes:
                writer.Write(_bytes.Span);
                return;
            case Kind.String when length > 4096:
                Encoding.UTF8.GetBytes(_string.AsSpan(), writer);
                return;
            default:
                var span = writer.GetSpan(length);
                CopyTo(span);
                writer.Advance(length);
                return;
        }
    }

    /// <summary>Returns the bytes directly when the value is stored as bytes.</summary>
    internal bool TryGetSpan(out ReadOnlySpan<byte> span)
    {
        if (_kind == Kind.Bytes)
        {
            span = _bytes.Span;
            return true;
        }

        span = default;
        return false;
    }

    internal static long? ParseInt64(ReadOnlySpan<byte> span) =>
        long.TryParse(span, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result) ? result : null;

    internal static double? ParseDouble(ReadOnlySpan<byte> span)
    {
        if (span.Length is >= 3 and <= 9)
        {
            Span<byte> lower = stackalloc byte[span.Length];
            for (var i = 0; i < span.Length; i++)
            {
                lower[i] = (byte)(span[i] | 0x20);
            }

            if (lower.SequenceEqual("inf"u8) || lower.SequenceEqual("+inf"u8) || lower.SequenceEqual("infinity"u8) || lower.SequenceEqual("+infinity"u8))
            {
                return double.PositiveInfinity;
            }

            if (lower.SequenceEqual("-inf"u8) || lower.SequenceEqual("-infinity"u8))
            {
                return double.NegativeInfinity;
            }

            if (lower.SequenceEqual("nan"u8) || lower.SequenceEqual("-nan"u8))
            {
                return double.NaN;
            }
        }

        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static string FormatDouble(double value)
    {
        if (double.IsPositiveInfinity(value))
        {
            return "+inf";
        }

        return double.IsNegativeInfinity(value) ? "-inf" : value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static InvalidCastException CastError(RedisValue value, string target) =>
        new(value.IsNull
            ? $"Cannot convert a nil Redis value to {target}."
            : $"The Redis value cannot be converted to {target}.");

    private double AsDouble() => BitConverter.Int64BitsToDouble(_bits);

    private int FormatNumber(Span<byte> destination)
    {
        if (_kind == Kind.Int64)
        {
            _bits.TryFormat(destination, out var written, default, CultureInfo.InvariantCulture);
            return written;
        }

        var d = AsDouble();
        if (double.IsPositiveInfinity(d))
        {
            "+inf"u8.CopyTo(destination);
            return 4;
        }

        if (double.IsNegativeInfinity(d))
        {
            "-inf"u8.CopyTo(destination);
            return 4;
        }

        d.TryFormat(destination, out var count, "R", CultureInfo.InvariantCulture);
        return count;
    }
}
