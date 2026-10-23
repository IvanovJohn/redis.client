namespace Redis.Client.IntegrationTests.Infrastructure;

/// <summary>
/// Shared server for non-destructive tests: plain and TLS ports, ACL user <see cref="RedisServer.AclUser"/>.
/// Tests isolate themselves with unique key prefixes instead of flushing the database.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private RedisServer? _server;

    public RedisServer Server => _server ?? throw new InvalidOperationException("Fixture is not initialized.");

    public async Task InitializeAsync() => _server = await RedisServer.StartAsync(new RedisServerOptions { Tls = true });

    public async Task DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    /// <summary>Unique key prefix for one test.</summary>
    public static string Prefix([System.Runtime.CompilerServices.CallerMemberName] string test = "") =>
        $"{test}:{Guid.NewGuid():N}:";
}

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "redis";
}
