namespace Redis.Client;

/// <summary>
/// The data received from the server is not valid RESP or exceeds the configured limits.
/// The connection is closed because request/response matching can no longer be trusted.
/// </summary>
public class RedisProtocolException : RedisException
{
    /// <summary>Initializes a new instance of the <see cref="RedisProtocolException"/> class.</summary>
    public RedisProtocolException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisProtocolException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public RedisProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisProtocolException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public RedisProtocolException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
