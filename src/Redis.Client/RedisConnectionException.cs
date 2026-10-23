namespace Redis.Client;

/// <summary>
/// The connection to Redis could not be established or was lost.
/// </summary>
/// <remarks>
/// When <see cref="MayHaveExecuted"/> is <see langword="true"/> the command had already been sent and the server may
/// have executed it. The client never retries such commands; only the caller knows whether a retry is safe.
/// </remarks>
public class RedisConnectionException : RedisException
{
    /// <summary>Initializes a new instance of the <see cref="RedisConnectionException"/> class.</summary>
    public RedisConnectionException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisConnectionException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public RedisConnectionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisConnectionException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public RedisConnectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisConnectionException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    /// <param name="mayHaveExecuted">Whether the command had been sent before the failure.</param>
    public RedisConnectionException(string message, Exception? innerException, bool mayHaveExecuted)
        : base(message, innerException)
    {
        MayHaveExecuted = mayHaveExecuted;
    }

    /// <summary>Gets a value indicating whether the command had been sent and may have been executed by the server.</summary>
    public bool MayHaveExecuted { get; }
}
