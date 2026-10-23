namespace Redis.Client;

/// <summary>
/// A command did not complete within its deadline (<see cref="RedisClientOptions.CommandTimeout"/>).
/// </summary>
/// <remarks>
/// If <see cref="MayHaveExecuted"/> is <see langword="false"/> the command was never sent. Otherwise it was sent and
/// the server may have executed it; its late reply is discarded.
/// </remarks>
public class RedisTimeoutException : RedisException
{
    /// <summary>Initializes a new instance of the <see cref="RedisTimeoutException"/> class.</summary>
    public RedisTimeoutException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisTimeoutException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public RedisTimeoutException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisTimeoutException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public RedisTimeoutException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisTimeoutException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="mayHaveExecuted">Whether the command had been sent before the deadline expired.</param>
    public RedisTimeoutException(string message, bool mayHaveExecuted)
        : base(message)
    {
        MayHaveExecuted = mayHaveExecuted;
    }

    /// <summary>Gets a value indicating whether the command had been sent and may have been executed by the server.</summary>
    public bool MayHaveExecuted { get; }
}
