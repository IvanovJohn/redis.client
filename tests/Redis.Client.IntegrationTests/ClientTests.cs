using Redis.Client.IntegrationTests.Infrastructure;

namespace Redis.Client.IntegrationTests;

[Collection(RedisCollection.Name)]
public sealed class ClientTests(RedisFixture fixture)
{
    [Fact]
    public async Task Execute_runs_commands_without_typed_api()
    {
        var stream = RedisFixture.Prefix() + "stream";
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        var id = await client.ExecuteAsync("XADD", [stream, "*", "field", "value"]);
        Assert.Equal(RedisResultType.BulkString, id.Type);
        Assert.Contains("-", (string?)id, StringComparison.Ordinal);

        var range = await client.ExecuteAsync("XRANGE", [stream, "-", "+"]);
        Assert.Equal(1, range.Count);
        Assert.Equal((string?)id, (string?)range[0][0]);
        Assert.Equal(["field", "value"], ((RedisResult[]?)range[0][1])!.Select(r => (string?)r));
    }

    [Fact]
    public async Task Execute_surfaces_server_errors()
    {
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        var error = await Assert.ThrowsAsync<RedisServerException>(() => client.ExecuteAsync("NOSUCHCOMMAND").AsTask());
        Assert.Equal("ERR", error.ErrorCode);
    }

    [Theory]
    [InlineData("SELECT")]
    [InlineData("multi")]
    [InlineData("WATCH")]
    [InlineData("SUBSCRIBE")]
    [InlineData("HELLO")]
    [InlineData("AUTH")]
    public async Task Execute_rejects_connection_state_commands(string command)
    {
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ExecuteAsync(command, ["x"]).AsTask());
    }

    [Fact]
    public async Task Execute_rejects_client_reply()
    {
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        await Assert.ThrowsAsync<NotSupportedException>(() => client.ExecuteAsync("CLIENT", ["REPLY", "OFF"]).AsTask());
    }

    [Fact]
    public async Task Execute_routes_xread_block_to_dedicated_connection()
    {
        var stream = RedisFixture.Prefix() + "s";
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        var read = client.ExecuteAsync("XREAD", ["BLOCK", 2000, "STREAMS", stream, "$"]).AsTask();
        await Task.Delay(200);
        Assert.Equal("PONG", (string?)await client.ExecuteAsync("PING"));
        await client.ExecuteAsync("XADD", [stream, "*", "f", "v"]);
        var result = await read;
        Assert.False(result.IsNull);
    }

    [Fact]
    public async Task Calls_after_dispose_throw()
    {
        var client = new RedisClient(fixture.Server.CreateOptions());
        await client.Strings.SetAsync(RedisFixture.Prefix(), "v");
        await client.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.Strings.GetAsync("k").AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync().AsTask());
    }

    [Fact]
    public async Task Dispose_waits_for_commands_already_sent()
    {
        var client = new RedisClient(fixture.Server.CreateOptions());
        await client.ConnectAsync();
        var sleeping = client.ExecuteAsync("DEBUG", ["SLEEP", "0.5"]).AsTask();
        await Task.Delay(100);
        await client.DisposeAsync();
        Assert.Equal("OK", (string?)await sleeping);
    }

    [Fact]
    public async Task Invalid_options_are_rejected_when_the_client_is_created()
    {
        var options = fixture.Server.CreateOptions(o => o.CommandTimeout = TimeSpan.Zero);
        var error = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => new RedisClient(options));
        Assert.Contains(error.Failures, f => f.Contains("CommandTimeout", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Connection_string_constructor_works()
    {
        var options = fixture.Server.CreateOptions();
        var endpoint = (System.Net.IPEndPoint)options.Endpoints[0];
        await using var client = new RedisClient($"redis://{RedisServer.AclUser}:{RedisServer.AclPassword}@127.0.0.1:{endpoint.Port}/1?clientName=cs-test");
        await client.Strings.SetAsync("cs-test-key", "v");
        Assert.Equal("v", await fixture.Server.CliAsync("-n", "1", "GET", "cs-test-key"));
    }
}
