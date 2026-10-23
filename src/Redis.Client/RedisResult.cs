using System.Buffers;
using System.Globalization;
using System.Text;
using Redis.Client.Protocol;

namespace Redis.Client;

/// <summary>Type of a reply returned by <see cref="IRedisCommands.ExecuteAsync"/>.</summary>
public enum RedisResultType
{
    /// <summary><c>nil</c> (RESP2 null bulk string or array, RESP3 null).</summary>
    Null = 0,

    /// <summary>A simple string such as <c>OK</c>.</summary>
    SimpleString,

    /// <summary>A binary-safe bulk string.</summary>
    BulkString,

    /// <summary>A verbatim string with a format (<c>txt</c>, <c>mkd</c>).</summary>
    VerbatimString,

    /// <summary>A 64-bit integer.</summary>
    Integer,

    /// <summary>A floating point number (RESP3).</summary>
    Double,

    /// <summary>A boolean (RESP3).</summary>
    Boolean,

    /// <summary>An arbitrary precision integer (RESP3), kept as text.</summary>
    BigNumber,

    /// <summary>An error nested in an aggregate (top-level errors are thrown as <see cref="RedisServerException"/>).</summary>
    Error,

    /// <summary>An array.</summary>
    Array,

    /// <summary>A set (RESP3).</summary>
    Set,

    /// <summary>A map (RESP3); <see cref="RedisResult.Elements"/> holds keys and values alternately.</summary>
    Map,

    /// <summary>An out-of-band push message (RESP3).</summary>
    Push,
}

/// <summary>
/// A reply of an arbitrary command, fully copied out of the network buffers. Scalars convert with explicit casts;
/// aggregates expose their elements.
/// </summary>
public sealed class RedisResult
{
    private static readonly RedisResult[] NoElements = [];

    private readonly RedisValue _value;
    private readonly RedisResult[] _elements;

    private RedisResult(RedisResultType type, RedisValue value, RedisResult[] elements, string? format, RedisResult? attributes)
    {
        Type = type;
        _value = value;
        _elements = elements;
        VerbatimFormat = format;
        Attributes = attributes;
    }

    /// <summary>Gets the reply type.</summary>
    public RedisResultType Type { get; }

    /// <summary>Gets a value indicating whether the reply is <c>nil</c>.</summary>
    public bool IsNull => Type == RedisResultType.Null;

    /// <summary>Gets a value indicating whether the reply is an array, set, map or push message.</summary>
    public bool IsAggregate => Type is RedisResultType.Array or RedisResultType.Set or RedisResultType.Map or RedisResultType.Push;

    /// <summary>Gets the elements of an aggregate; maps alternate keys and values. Empty for scalars.</summary>
    public IReadOnlyList<RedisResult> Elements => _elements;

    /// <summary>Gets the number of elements of an aggregate (for maps: the number of keys and values).</summary>
    public int Count => _elements.Length;

    /// <summary>Gets the format of a verbatim string (e.g. <c>txt</c>).</summary>
    public string? VerbatimFormat { get; }

    /// <summary>Gets the RESP3 attributes sent before this reply, as a map, if any.</summary>
    public RedisResult? Attributes { get; }

    /// <summary>Gets the error text of a nested error.</summary>
    public string? ErrorMessage => Type == RedisResultType.Error ? (string?)_value : null;

    /// <summary>Gets an element of an aggregate.</summary>
    /// <param name="index">The element index.</param>
    public RedisResult this[int index] => _elements[index];

    /// <summary>Converts a scalar to a string (<see langword="null"/> for <c>nil</c>).</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator string?(RedisResult result) => (string?)result.AsValue();

    /// <summary>Copies the bytes of a scalar (<see langword="null"/> for <c>nil</c>).</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator byte[]?(RedisResult result) => (byte[]?)result.AsValue();

    /// <summary>Converts a scalar to an integer.</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator long(RedisResult result) => (long)result.AsValue();

    /// <summary>Converts a scalar to an integer (<see langword="null"/> for <c>nil</c>).</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator long?(RedisResult result) => (long?)result.AsValue();

    /// <summary>Converts a scalar to a number.</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator double(RedisResult result) => (double)result.AsValue();

    /// <summary>Converts a scalar to a number (<see langword="null"/> for <c>nil</c>).</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator double?(RedisResult result) => (double?)result.AsValue();

    /// <summary>Converts a boolean, integer (non-zero is true) or <c>OK</c> reply to a boolean.</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator bool(RedisResult result) => result.Type switch
    {
        RedisResultType.SimpleString => (string?)result._value == "OK" || (long)result._value != 0,
        _ => (long)result.AsValue() != 0,
    };

    /// <summary>Returns a scalar as a <see cref="RedisValue"/>.</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator RedisValue(RedisResult result) => result.AsValue();

    /// <summary>Returns the elements of an aggregate (<see langword="null"/> for <c>nil</c>).</summary>
    /// <param name="result">The reply.</param>
    public static explicit operator RedisResult[]?(RedisResult result) =>
        result.IsNull ? null : result.IsAggregate ? result._elements : throw new InvalidCastException($"A {result.Type} reply is not an aggregate.");

    /// <summary>Returns a scalar as a <see cref="RedisValue"/>.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="RedisServerException">The reply is a nested error.</exception>
    /// <exception cref="InvalidCastException">The reply is an aggregate.</exception>
    public RedisValue AsValue()
    {
        if (Type == RedisResultType.Error)
        {
            throw new RedisServerException((string?)_value ?? "ERR");
        }

        return IsAggregate ? throw new InvalidCastException($"A {Type} reply cannot be converted to a single value.") : _value;
    }

    /// <summary>
    /// Returns key/value pairs of a map, or of an array that holds keys and values alternately (the RESP2 form of
    /// the same reply).
    /// </summary>
    /// <returns>The pairs.</returns>
    public IReadOnlyList<KeyValuePair<RedisResult, RedisResult>> ToPairs()
    {
        if (!IsAggregate || _elements.Length % 2 != 0)
        {
            throw new InvalidCastException($"A {Type} reply with {_elements.Length} elements cannot be read as pairs.");
        }

        var pairs = new KeyValuePair<RedisResult, RedisResult>[_elements.Length / 2];
        for (var i = 0; i < pairs.Length; i++)
        {
            pairs[i] = new(_elements[2 * i], _elements[(2 * i) + 1]);
        }

        return pairs;
    }

    /// <summary>Returns a map (or alternating array) as a dictionary keyed by string.</summary>
    /// <returns>The dictionary.</returns>
    public IReadOnlyDictionary<string, RedisResult> ToDictionary()
    {
        var dictionary = new Dictionary<string, RedisResult>(StringComparer.Ordinal);
        foreach (var (key, value) in ToPairs())
        {
            dictionary[(string?)key ?? string.Empty] = value;
        }

        return dictionary;
    }

    /// <summary>Returns a readable representation for diagnostics.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        Append(builder, this);
        return builder.ToString();
    }

    internal static RedisResult From(RespFrame frame) =>
        From(frame, frame.HasAttributes ? From(frame.GetAttributeMap(), null) : null);

    private static RedisResult From(RespFrame frame, RedisResult? attributes)
    {
        switch (frame.Type)
        {
            case RespType.Null:
                return new(RedisResultType.Null, RedisValue.Null, NoElements, null, attributes);
            case RespType.Array:
            case RespType.Set:
            case RespType.Map:
            case RespType.Push:
            {
                var elements = new RedisResult[frame.ChildCount];
                var i = 0;
                foreach (var child in frame.GetChildren())
                {
                    elements[i++] = From(child, null);
                }

                var type = frame.Type switch
                {
                    RespType.Array => RedisResultType.Array,
                    RespType.Set => RedisResultType.Set,
                    RespType.Map => RedisResultType.Map,
                    _ => RedisResultType.Push,
                };
                return new(type, RedisValue.Null, elements, null, attributes);
            }

            case RespType.Integer:
                return new(RedisResultType.Integer, frame.GetInt64(), NoElements, null, attributes);
            case RespType.Double:
                return new(RedisResultType.Double, frame.GetDouble(), NoElements, null, attributes);
            case RespType.Boolean:
                return new(RedisResultType.Boolean, frame.GetBoolean() ? 1L : 0L, NoElements, null, attributes);
            case RespType.Error:
            case RespType.BulkError:
                return new(RedisResultType.Error, frame.GetString(), NoElements, null, attributes);
            case RespType.SimpleString:
                return new(RedisResultType.SimpleString, frame.GetString(), NoElements, null, attributes);
            case RespType.BigNumber:
                return new(RedisResultType.BigNumber, frame.GetString(), NoElements, null, attributes);
            case RespType.VerbatimString:
                return new(RedisResultType.VerbatimString, frame.Payload.ToArray(), NoElements, frame.GetVerbatimFormat(), attributes);
            default:
                return new(RedisResultType.BulkString, frame.Payload.ToArray(), NoElements, null, attributes);
        }
    }

    private static void Append(StringBuilder builder, RedisResult result)
    {
        switch (result.Type)
        {
            case RedisResultType.Null:
                builder.Append("(nil)");
                break;
            case RedisResultType.Error:
                builder.Append("(error) ").Append(result.ErrorMessage);
                break;
            case RedisResultType.Array or RedisResultType.Set or RedisResultType.Map or RedisResultType.Push:
                builder.Append(result.Type == RedisResultType.Map ? '{' : '[');
                for (var i = 0; i < result._elements.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(result.Type == RedisResultType.Map && i % 2 == 1 ? ": " : ", ");
                    }

                    Append(builder, result._elements[i]);
                }

                builder.Append(result.Type == RedisResultType.Map ? '}' : ']');
                break;
            case RedisResultType.Integer:
                builder.Append(((long)result._value).ToString(CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append((string?)result._value);
                break;
        }
    }
}
