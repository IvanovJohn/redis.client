using System.Net;
using BenchmarkDotNet.Attributes;
using StackExchange.Redis;

namespace Redis.Client.Benchmarks;

/// <summary>
/// End-to-end GET/SET throughput against a real server, with StackExchange.Redis as a reference point.
/// Server: <c>REDIS_BENCH_ENDPOINT</c> (default <c>127.0.0.1:6379</c>, no authentication).
/// Each invocation runs <see cref="Operations"/> commands with the given number of concurrent callers.
/// </summary>
[MemoryDiagnoser]
public class ClientBenchmarks
{
    private const int Operations = 10_000;

    private RedisClient _client = null!;
    private ConnectionMultiplexer _multiplexer = null!;
    private IDatabase _database = null!;

    [Params(1, 16, 256)]
    public int Concurrency { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        var endpoint = Environment.GetEnvironmentVariable("REDIS_BENCH_ENDPOINT") ?? "127.0.0.1:6379";
        var options = new RedisClientOptions();
        options.Endpoints.Add(IPEndPoint.Parse(endpoint));
        _client = new RedisClient(options);
        await _client.ConnectAsync();
        await _client.Strings.SetAsync("bench:key", new string('x', 100));

        _multiplexer = await ConnectionMultiplexer.ConnectAsync(endpoint);
        _database = _multiplexer.GetDatabase();
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _client.DisposeAsync();
        await _multiplexer.DisposeAsync();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = Operations)]
    public Task StackExchangeRedis_Get() => Run(() => _database.StringGetAsync("bench:key"));

    [Benchmark(OperationsPerInvoke = Operations)]
    public Task RedisClient_Get() => Run(() => _client.Strings.GetAsync("bench:key").AsTask());

    [Benchmark(OperationsPerInvoke = Operations)]
    public Task StackExchangeRedis_Set() => Run(() => _database.StringSetAsync("bench:set", "value"));

    [Benchmark(OperationsPerInvoke = Operations)]
    public Task RedisClient_Set() => Run(() => _client.Strings.SetAsync("bench:set", "value").AsTask());

    private Task Run<T>(Func<Task<T>> operation)
    {
        var perCaller = Operations / Concurrency;
        return Task.WhenAll(Enumerable.Range(0, Concurrency).Select(async _ =>
        {
            for (var i = 0; i < perCaller; i++)
            {
                await operation().ConfigureAwait(false);
            }
        }));
    }
}
