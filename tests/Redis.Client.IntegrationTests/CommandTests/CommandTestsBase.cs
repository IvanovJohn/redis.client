using Redis.Client.IntegrationTests.Infrastructure;

namespace Redis.Client.IntegrationTests.CommandTests;

/// <summary>Runs every command test once over RESP2 and once over RESP3: typed results must not depend on the protocol.</summary>
public abstract class CommandTestsBase(RedisFixture fixture, RedisProtocol protocol) : IAsyncLifetime
{
    private RedisClient? _client;

    protected RedisClient Client => _client ?? throw new InvalidOperationException();

    protected RedisFixture Fixture => fixture;

    protected string Prefix { get; } = $"{protocol}:{Guid.NewGuid():N}:";

    public async Task InitializeAsync()
    {
        _client = new RedisClient(fixture.Server.CreateOptions(o => o.Protocol = protocol));
        await _client.ConnectAsync();
        Assert.Equal(protocol == RedisProtocol.Resp2 ? 2 : 3, _client.Router.Multiplexed[0].Connection!.Protocol);
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    protected RedisKey Key(string name) => Prefix + name;
}
