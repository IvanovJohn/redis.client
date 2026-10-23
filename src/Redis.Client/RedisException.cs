namespace Redis.Client;

/// <summary>Base type for all errors raised by the Redis client.</summary>
public class RedisException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RedisException"/> class.</summary>
    public RedisException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public RedisException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public RedisException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
