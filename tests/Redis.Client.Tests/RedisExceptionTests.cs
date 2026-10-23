namespace Redis.Client.Tests;

public sealed class RedisExceptionTests
{
    [Theory]
    [InlineData("WRONGTYPE Operation against a key holding the wrong kind of value", "WRONGTYPE")]
    [InlineData("NOSCRIPT No matching script. Please use EVAL.", "NOSCRIPT")]
    [InlineData("ERR unknown command 'FOO'", "ERR")]
    [InlineData("EXECABORT Transaction discarded because of previous errors.", "EXECABORT")]
    [InlineData("boom", "ERR")]
    [InlineData("", "ERR")]
    public void Server_error_code_is_first_uppercase_word(string message, string code)
    {
        var error = new RedisServerException(message);
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void Authentication_error_is_a_connection_error_that_did_not_execute()
    {
        RedisConnectionException error = new RedisAuthenticationException("WRONGPASS invalid username-password pair");
        Assert.False(error.MayHaveExecuted);
        Assert.IsAssignableFrom<RedisException>(error);
    }
}
