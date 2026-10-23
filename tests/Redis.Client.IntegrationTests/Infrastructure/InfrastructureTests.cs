namespace Redis.Client.IntegrationTests.Infrastructure;

/// <summary>Sanity checks of the test environment itself (container, ACL user, TLS listener).</summary>
[Collection(RedisCollection.Name)]
public sealed class InfrastructureTests(RedisFixture fixture)
{
    [Fact]
    public async Task Server_answers_ping_as_acl_user()
    {
        Assert.Equal("PONG", await fixture.Server.CliAsync("PING"));
        Assert.Equal(RedisServer.AclUser, await fixture.Server.CliAsync("ACL", "WHOAMI"));
    }

    [Fact]
    public async Task Server_answers_ping_over_tls()
    {
        Assert.Equal("PONG", await fixture.Server.CliTlsAsync("PING"));
    }

    [Fact]
    public async Task Server_runs_expected_version()
    {
        var info = await fixture.Server.CliAsync("INFO", "server");
        var expected = RedisServer.ImageVersion;
        Assert.Contains($"redis_version:{expected.Major}.{expected.Minor}.", info);
    }
}
