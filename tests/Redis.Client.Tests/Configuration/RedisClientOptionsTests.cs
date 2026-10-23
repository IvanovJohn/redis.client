using System.Net;
using Microsoft.Extensions.Options;

namespace Redis.Client.Tests.Configuration;

public sealed class RedisClientOptionsTests
{
    [Fact]
    public void Defaults_match_the_spec()
    {
        var options = new RedisClientOptions();
        Assert.Equal(0, options.Database);
        Assert.Equal(RedisProtocol.Auto, options.Protocol);
        Assert.False(options.Ssl);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.CommandTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(100), options.ReconnectBaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ReconnectMaxDelay);
        Assert.Equal(BacklogPolicy.Queue, options.BacklogPolicy);
        Assert.Equal(1, options.MultiplexedConnectionCount);
        Assert.Equal(16, options.DedicatedPoolSize);
        Assert.Equal(512L * 1024 * 1024, options.MaxBulkLength);
        Assert.Equal(64, options.MaxNestingDepth);
        Assert.Equal(1024, options.SubscriptionBufferSize);
        Assert.Equal(SubscriptionOverflowPolicy.DropOldest, options.SubscriptionOverflowPolicy);
        Assert.False(options.RecordCommandArguments);
    }

    [Fact]
    public void Full_connection_string_is_parsed()
    {
        var options = RedisClientOptions.FromConnectionString("rediss://app:p%40ss@redis.prod:6380/2?clientName=orders&commandTimeout=00:00:02");
        Assert.True(options.Ssl);
        Assert.Equal("app", options.Username);
        Assert.Equal("p@ss", options.Password);
        var endpoint = Assert.IsType<DnsEndPoint>(Assert.Single(options.Endpoints));
        Assert.Equal("redis.prod", endpoint.Host);
        Assert.Equal(6380, endpoint.Port);
        Assert.Equal(2, options.Database);
        Assert.Equal("orders", options.ClientName);
        Assert.Equal(TimeSpan.FromSeconds(2), options.CommandTimeout);
    }

    [Fact]
    public void Minimal_connection_string_uses_defaults()
    {
        var options = RedisClientOptions.FromConnectionString("redis://localhost");
        Assert.False(options.Ssl);
        var endpoint = Assert.IsType<DnsEndPoint>(Assert.Single(options.Endpoints));
        Assert.Equal(6379, endpoint.Port);
        Assert.Null(options.Username);
        Assert.Null(options.Password);
        Assert.Equal(0, options.Database);
    }

    [Theory]
    [InlineData("redis://127.0.0.1:7000", "127.0.0.1:7000")]
    [InlineData("redis://[::1]:7000", "[::1]:7000")]
    public void Ip_addresses_become_ip_endpoints(string connectionString, string expected)
    {
        var endpoint = Assert.IsType<IPEndPoint>(Assert.Single(RedisClientOptions.FromConnectionString(connectionString).Endpoints));
        Assert.Equal(expected, endpoint.ToString());
    }

    [Fact]
    public void Password_without_user_is_for_default_user()
    {
        var options = RedisClientOptions.FromConnectionString("redis://:secret@host");
        Assert.Null(options.Username);
        Assert.Equal("secret", options.Password);
        Assert.Equal("secret", RedisClientOptions.FromConnectionString("redis://secret@host").Password);
    }

    [Fact]
    public void Query_options_are_case_insensitive_and_typed()
    {
        var options = RedisClientOptions.FromConnectionString(
            "redis://h?PROTOCOL=resp2&BacklogPolicy=failfast&connectTimeout=1500&multiplexedConnectionCount=4&dedicatedPoolSize=3" +
            "&maxBulkLength=1024&maxNestingDepth=8&subscriptionBufferSize=10&subscriptionOverflowPolicy=Wait&recordCommandArguments=true" +
            "&keepAliveInterval=00:00:10&reconnectBaseDelay=50&reconnectMaxDelay=00:00:05");
        Assert.Equal(RedisProtocol.Resp2, options.Protocol);
        Assert.Equal(BacklogPolicy.FailFast, options.BacklogPolicy);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), options.ConnectTimeout);
        Assert.Equal(4, options.MultiplexedConnectionCount);
        Assert.Equal(3, options.DedicatedPoolSize);
        Assert.Equal(1024, options.MaxBulkLength);
        Assert.Equal(8, options.MaxNestingDepth);
        Assert.Equal(10, options.SubscriptionBufferSize);
        Assert.Equal(SubscriptionOverflowPolicy.Wait, options.SubscriptionOverflowPolicy);
        Assert.True(options.RecordCommandArguments);
        Assert.Equal(TimeSpan.FromSeconds(10), options.KeepAliveInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(50), options.ReconnectBaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ReconnectMaxDelay);
    }

    [Theory]
    [InlineData("http://user:TopSecret1@host")]
    [InlineData("redis://user:TopSecret1@host?unknownOption=1")]
    [InlineData("redis://user:TopSecret1@host?commandTimeout=soon")]
    [InlineData("redis://user:TopSecret1@host/abc")]
    [InlineData("redis://user:TopSecret1@host?backlogPolicy=7")]
    [InlineData("not a uri TopSecret1")]
    public void Invalid_connection_string_throws_without_leaking_password(string connectionString)
    {
        var error = Assert.Throws<FormatException>(() => RedisClientOptions.FromConnectionString(connectionString));
        Assert.DoesNotContain("TopSecret1", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_masks_password()
    {
        var options = RedisClientOptions.FromConnectionString("redis://app:TopSecret1@host");
        var text = options.ToString();
        Assert.Contains("Password=***", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TopSecret1", text, StringComparison.Ordinal);
        Assert.Equal("app:***", new RedisCredentials("app", "TopSecret1").ToString());
    }

    [Fact]
    public void Clone_is_independent()
    {
        var options = RedisClientOptions.FromConnectionString("redis://host?clientName=a");
        var clone = options.Clone();
        options.ClientName = "b";
        options.Endpoints.Add(new DnsEndPoint("other", 1));
        Assert.Equal("a", clone.ClientName);
        Assert.Single(clone.Endpoints);
    }

    [Fact]
    public void Valid_options_pass_validation()
    {
        var result = new RedisClientOptionsValidator().Validate(null, RedisClientOptions.FromConnectionString("redis://localhost"));
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Zero_command_timeout_is_rejected_with_parameter_name()
    {
        var options = RedisClientOptions.FromConnectionString("redis://localhost");
        options.CommandTimeout = TimeSpan.Zero;
        var error = Assert.Throws<OptionsValidationException>(() => RedisClientOptionsValidator.ThrowIfInvalid(options));
        Assert.Contains(error.Failures, f => f.Contains("CommandTimeout", StringComparison.Ordinal));
    }

    [Fact]
    public void Password_and_credentials_provider_conflict()
    {
        var options = RedisClientOptions.FromConnectionString("redis://:pw@localhost");
        options.CredentialsProvider = _ => ValueTask.FromResult(new RedisCredentials(null, "pw"));
        AssertInvalid(options, "CredentialsProvider");
    }

    [Fact]
    public void Several_endpoints_are_not_supported_yet()
    {
        var options = RedisClientOptions.FromConnectionString("redis://localhost");
        options.Endpoints.Add(new DnsEndPoint("other", 6379));
        AssertInvalid(options, "not supported");
    }

    [Theory]
    [InlineData("Endpoints")]
    [InlineData("Database")]
    [InlineData("DedicatedPoolSize")]
    [InlineData("MultiplexedConnectionCount")]
    [InlineData("ReconnectBaseDelay")]
    [InlineData("Username")]
    [InlineData("SslOptions")]
    [InlineData("Protocol")]
    public void Invalid_values_are_reported(string parameter)
    {
        var options = RedisClientOptions.FromConnectionString("redis://localhost");
        switch (parameter)
        {
            case "Endpoints":
                options.Endpoints.Clear();
                break;
            case "Database":
                options.Database = -1;
                break;
            case "DedicatedPoolSize":
                options.DedicatedPoolSize = 0;
                break;
            case "MultiplexedConnectionCount":
                options.MultiplexedConnectionCount = 0;
                break;
            case "ReconnectBaseDelay":
                options.ReconnectBaseDelay = TimeSpan.FromMinutes(5);
                break;
            case "Username":
                options.Username = "app";
                break;
            case "SslOptions":
                options.SslOptions = new System.Net.Security.SslClientAuthenticationOptions();
                break;
            case "Protocol":
                options.Protocol = (RedisProtocol)42;
                break;
        }

        AssertInvalid(options, parameter);
    }

    private static void AssertInvalid(RedisClientOptions options, string expectedFragment)
    {
        var result = new RedisClientOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(expectedFragment, StringComparison.Ordinal));
    }
}
