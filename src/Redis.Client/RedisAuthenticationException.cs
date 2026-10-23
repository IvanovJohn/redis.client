namespace Redis.Client;

/// <summary>
/// The server rejected the credentials (wrong password, unknown or disabled ACL user).
/// The message never contains the password.
/// </summary>
public class RedisAuthenticationException : RedisConnectionException
{
    /// <summary>Initializes a new instance of the <see cref="RedisAuthenticationException"/> class.</summary>
    public RedisAuthenticationException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisAuthenticationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public RedisAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisAuthenticationException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public RedisAuthenticationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
