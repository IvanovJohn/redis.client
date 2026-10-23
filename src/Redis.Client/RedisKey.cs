using System.Text;

namespace Redis.Client;

/// <summary>
/// A Redis key. Created implicitly from <see cref="string"/> (encoded as UTF-8), <see cref="byte"/> arrays
/// or <see cref="ReadOnlyMemory{T}"/> of bytes. Keys are binary-safe.
/// </summary>
/// <remarks>
/// Two keys are equal when their byte representations are equal, regardless of how they were created.
/// The default value represents "no key" and is rejected when a command is sent.
/// </remarks>
public readonly struct RedisKey : IEquatable<RedisKey>
{
    private readonly string? _string;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly bool _isBinary;

    private RedisKey(string value)
    {
        _string = value;
        _bytes = default;
        _isBinary = false;
    }

    private RedisKey(ReadOnlyMemory<byte> value)
    {
        _string = null;
        _bytes = value;
        _isBinary = true;
    }

    /// <summary>Gets a value indicating whether this is the default (missing) key.</summary>
    public bool IsNull => !_isBinary && _string is null;

    /// <summary>Creates a key from a string encoded as UTF-8.</summary>
    /// <param name="value">The key; <see langword="null"/> produces the default (missing) key.</param>
    public static implicit operator RedisKey(string? value) => value is null ? default : new RedisKey(value);

    /// <summary>Creates a key from raw bytes without copying them.</summary>
    /// <param name="value">The key; <see langword="null"/> produces the default (missing) key.</param>
    public static implicit operator RedisKey(byte[]? value) => value is null ? default : new RedisKey(value);

    /// <summary>Creates a key from raw bytes without copying them.</summary>
    /// <param name="value">The key bytes.</param>
    public static implicit operator RedisKey(ReadOnlyMemory<byte> value) => new(value);

    /// <summary>Returns the key as a string, decoding binary keys as UTF-8.</summary>
    /// <param name="key">The key.</param>
    public static explicit operator string?(RedisKey key) => key.IsNull ? null : key.ToString();

    /// <summary>Returns a copy of the key bytes.</summary>
    /// <param name="key">The key.</param>
    public static explicit operator byte[]?(RedisKey key)
    {
        if (key.IsNull)
        {
            return null;
        }

        return key._isBinary ? key._bytes.ToArray() : Encoding.UTF8.GetBytes(key._string!);
    }

    /// <summary>Converts the key to a value, e.g. to pass it as a command argument.</summary>
    /// <param name="key">The key.</param>
    public static implicit operator RedisValue(RedisKey key)
    {
        if (key.IsNull)
        {
            return RedisValue.Null;
        }

        return key._isBinary ? key._bytes : (RedisValue)key._string;
    }

    /// <summary>Compares two keys by their byte representation.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    public static bool operator ==(RedisKey left, RedisKey right) => left.Equals(right);

    /// <summary>Compares two keys by their byte representation.</summary>
    /// <param name="left">The first key.</param>
    /// <param name="right">The second key.</param>
    public static bool operator !=(RedisKey left, RedisKey right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(RedisKey other)
    {
        if (IsNull || other.IsNull)
        {
            return IsNull && other.IsNull;
        }

        if (!_isBinary && !other._isBinary)
        {
            return string.Equals(_string, other._string, StringComparison.Ordinal);
        }

        return ByteHelpers.WithBytes(this, other, static (a, b) => a.SequenceEqual(b));
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RedisKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => IsNull ? 0 : ByteHelpers.WithBytes(this, static bytes => ByteHelpers.Hash(bytes));

    /// <summary>Returns the key as text; binary keys are decoded as UTF-8.</summary>
    /// <returns>The key text, or an empty string for the default key.</returns>
    public override string ToString()
    {
        if (IsNull)
        {
            return string.Empty;
        }

        return _isBinary ? Encoding.UTF8.GetString(_bytes.Span) : _string!;
    }

    /// <summary>Number of bytes the key occupies on the wire.</summary>
    internal int GetByteCount() => _isBinary ? _bytes.Length : Encoding.UTF8.GetByteCount(_string ?? string.Empty);

    /// <summary>Writes the key bytes into <paramref name="destination"/>, which must be large enough.</summary>
    internal int CopyTo(Span<byte> destination)
    {
        if (_isBinary)
        {
            _bytes.Span.CopyTo(destination);
            return _bytes.Length;
        }

        return Encoding.UTF8.GetBytes(_string ?? string.Empty, destination);
    }
}
