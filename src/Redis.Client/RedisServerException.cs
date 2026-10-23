namespace Redis.Client;

/// <summary>
/// An error reply returned by the Redis server, such as <c>WRONGTYPE</c> or <c>NOSCRIPT</c>.
/// A server error never breaks the connection.
/// </summary>
public class RedisServerException : RedisException
{
    /// <summary>Initializes a new instance of the <see cref="RedisServerException"/> class.</summary>
    public RedisServerException()
        : this("ERR")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisServerException"/> class.</summary>
    /// <param name="message">The full error text sent by the server, e.g. <c>WRONGTYPE Operation against a key ...</c>.</param>
    public RedisServerException(string message)
        : base(message)
    {
        ErrorCode = ParseErrorCode(message);
    }

    /// <summary>Initializes a new instance of the <see cref="RedisServerException"/> class.</summary>
    /// <param name="message">The full error text sent by the server.</param>
    /// <param name="innerException">The underlying error.</param>
    public RedisServerException(string message, Exception? innerException)
        : base(message, innerException)
    {
        ErrorCode = ParseErrorCode(message);
    }

    /// <summary>
    /// Gets the error code: the first word of the server message (<c>ERR</c>, <c>WRONGTYPE</c>, <c>NOAUTH</c>, ...).
    /// </summary>
    public string ErrorCode { get; }

    private static string ParseErrorCode(string message)
    {
        var space = message.IndexOf(' ', StringComparison.Ordinal);
        var word = space < 0 ? message : message[..space];
        return word.Length > 0 && word.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_' || c == '-')
            ? word
            : "ERR";
    }
}
