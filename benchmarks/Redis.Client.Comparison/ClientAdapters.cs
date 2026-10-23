using System.Net;
using StackExchange.Redis;

namespace Redis.Client.Comparison;

/// <summary>
/// The same minimal surface over each client. Every member is an <c>async</c> wrapper on both sides,
/// so the adapter overhead is identical and does not favour either client.
/// </summary>
internal interface IClientAdapter : IAsyncDisposable
{
    string Name { get; }

    Task ConnectAsync();

    ValueTask SetAsync(string key, byte[] value);

    /// <summary>GET, returning the value length (-1 for nil) without converting the payload.</summary>
    ValueTask<long> GetLengthAsync(string key);

    /// <summary>GET, comparing the value with <paramref name="expected"/> byte by byte.</summary>
    ValueTask<bool> GetMatchesAsync(string key, byte[] expected);
}

/// <summary>Timeouts shared by both clients so that failure behaviour is compared on equal terms.</summary>
internal sealed record ClientSettings(IPEndPoint Endpoint, TimeSpan ConnectTimeout, TimeSpan CommandTimeout);

internal sealed class RedisClientAdapter(ClientSettings settings) : IClientAdapter
{
    private RedisClient _client = null!;

    public string Name => "Redis.Client";

    public async Task ConnectAsync()
    {
        var options = new RedisClientOptions
        {
            ConnectTimeout = settings.ConnectTimeout,
            CommandTimeout = settings.CommandTimeout,
        };
        options.Endpoints.Add(settings.Endpoint);
        _client = new RedisClient(options);
        await _client.ConnectAsync();
    }

    public async ValueTask SetAsync(string key, byte[] value) => await _client.Strings.SetAsync(key, value);

    public async ValueTask<long> GetLengthAsync(string key)
    {
        var value = await _client.Strings.GetAsync(key);
        return value.IsNull ? -1 : ((byte[])value!).Length;
    }

    public async ValueTask<bool> GetMatchesAsync(string key, byte[] expected) =>
        await _client.Strings.GetAsync(key) == expected;

    public ValueTask DisposeAsync() => _client?.DisposeAsync() ?? ValueTask.CompletedTask;
}

internal sealed class StackExchangeAdapter(ClientSettings settings) : IClientAdapter
{
    private ConnectionMultiplexer _multiplexer = null!;
    private IDatabase _database = null!;

    public string Name => "StackExchange.Redis";

    public async Task ConnectAsync()
    {
        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = (int)settings.ConnectTimeout.TotalMilliseconds,
            SyncTimeout = (int)settings.CommandTimeout.TotalMilliseconds,
            AsyncTimeout = (int)settings.CommandTimeout.TotalMilliseconds,
        };
        options.EndPoints.Add(settings.Endpoint);
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        _database = _multiplexer.GetDatabase();
    }

    public async ValueTask SetAsync(string key, byte[] value) => await _database.StringSetAsync(key, value);

    public async ValueTask<long> GetLengthAsync(string key)
    {
        var value = await _database.StringGetAsync(key);
        return value.IsNull ? -1 : value.Length();
    }

    public async ValueTask<bool> GetMatchesAsync(string key, byte[] expected) =>
        await _database.StringGetAsync(key) == expected;

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }
    }
}
