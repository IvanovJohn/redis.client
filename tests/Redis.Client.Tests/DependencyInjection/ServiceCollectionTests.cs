using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Redis.Client.Tests.Fakes;

namespace Redis.Client.Tests.DependencyInjection;

public sealed class ServiceCollectionTests
{
    [Fact]
    public async Task Client_is_configured_from_a_configuration_section()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Redis:ConnectionString"] = "redis://app:secret@redis.prod:6380/2",
            ["Redis:CommandTimeout"] = "00:00:02",
            ["Redis:ClientName"] = "orders",
            ["Redis:BacklogPolicy"] = "FailFast",
        }).Build();
        var services = new ServiceCollection();
        services.AddRedisClient(configuration.GetSection("Redis"));
        await using var provider = services.BuildServiceProvider();

        var client = Assert.IsType<RedisClient>(provider.GetRequiredService<IRedisClient>());
        Assert.Same(client, provider.GetRequiredService<IRedisClient>());
        Assert.Equal(TimeSpan.FromSeconds(2), client.Options.CommandTimeout);
        Assert.Equal("orders", client.Options.ClientName);
        Assert.Equal(BacklogPolicy.FailFast, client.Options.BacklogPolicy);
        Assert.Equal(2, client.Options.Database);
        Assert.Equal("app", client.Options.Username);
        Assert.Equal("redis.prod", Assert.IsType<DnsEndPoint>(Assert.Single(client.Options.Endpoints)).Host);
    }

    [Fact]
    public async Task Endpoints_and_individual_options_can_be_configured()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Endpoints:0"] = "10.0.0.5:7000",
            ["Password"] = "pw",
            ["Ssl"] = "true",
            ["DedicatedPoolSize"] = "4",
        }).Build();
        var services = new ServiceCollection();
        services.AddRedisClient(configuration);
        await using var provider = services.BuildServiceProvider();

        var client = (RedisClient)provider.GetRequiredService<IRedisClient>();
        Assert.Equal("10.0.0.5:7000", Assert.IsType<IPEndPoint>(Assert.Single(client.Options.Endpoints)).ToString());
        Assert.True(client.Options.Ssl);
        Assert.Equal("pw", client.Options.Password);
        Assert.Equal(4, client.Options.DedicatedPoolSize);
    }

    [Fact]
    public async Task Unknown_configuration_key_fails_at_resolution()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionString"] = "redis://localhost",
            ["CommandTimeuot"] = "00:00:01",
        }).Build();
        var services = new ServiceCollection();
        services.AddRedisClient(configuration);
        await using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<FormatException>(() => provider.GetRequiredService<IRedisClient>());
        Assert.Contains("CommandTimeuot", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_options_fail_validation()
    {
        var services = new ServiceCollection();
        services.AddRedisClient("redis://localhost", o => o.CommandTimeout = TimeSpan.Zero);
        await using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IRedisClient>());
    }

    [Fact]
    public async Task Keyed_clients_are_independent()
    {
        var services = new ServiceCollection();
        services.AddKeyedRedisClient("cache", "redis://cache-host");
        services.AddKeyedRedisClient("queue", o => o.Endpoints.Add(new DnsEndPoint("queue-host", 6380)));
        await using var provider = services.BuildServiceProvider();

        var cache = (RedisClient)provider.GetRequiredKeyedService<IRedisClient>("cache");
        var queue = (RedisClient)provider.GetRequiredKeyedService<IRedisClient>("queue");
        Assert.NotSame(cache, queue);
        Assert.Equal("cache-host", ((DnsEndPoint)cache.Options.Endpoints[0]).Host);
        Assert.Equal("queue-host", ((DnsEndPoint)queue.Options.Endpoints[0]).Host);
        Assert.Null(provider.GetService<IRedisClient>());
    }

    [Fact]
    public async Task Client_is_disposed_with_the_container()
    {
        await using var server = new FakeRedisServer();
        var services = new ServiceCollection();
        services.AddRedisClient(o => o.Endpoints.Add(new IPEndPoint(IPAddress.Loopback, server.Port)));
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IRedisClient>();
        await client.ConnectAsync();
        Assert.Equal(1, server.ConnectionCount);

        await provider.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.Strings.GetAsync("k").AsTask());
        await Connections.MultiplexedConnectionTests.WaitUntilAsync(() => server.ConnectionCount == 0);
    }

    [Fact]
    public async Task Synchronous_container_disposal_is_supported()
    {
        var services = new ServiceCollection();
        services.AddRedisClient("redis://localhost");
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IRedisClient>();
        provider.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync().AsTask());
    }
}
