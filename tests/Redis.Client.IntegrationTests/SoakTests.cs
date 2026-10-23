using System.Globalization;
using Microsoft.Extensions.Logging;
using Redis.Client.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace Redis.Client.IntegrationTests;

/// <summary>
/// Long-running concurrency test under connection chaos. Every worker writes a unique value to its own key and
/// reads it back; if a reply were ever delivered to the wrong caller, a worker would read another worker's value.
/// Duration: <c>SOAK_SECONDS</c> (default 20).
/// </summary>
[Trait("Category", "Soak")]
public sealed class SoakTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Replies_never_reach_the_wrong_caller_under_chaos()
    {
        var seconds = int.TryParse(Environment.GetEnvironmentVariable("SOAK_SECONDS"), CultureInfo.InvariantCulture, out var s) ? s : 20;
        var logs = new ListLoggerProvider();
        using var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddProvider(logs));
        await using var server = await RedisServer.StartAsync();
        await using var client = new RedisClient(server.CreateOptions(o =>
        {
            o.MultiplexedConnectionCount = 2;
            o.CommandTimeout = TimeSpan.FromSeconds(2);
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(20);
            o.ReconnectMaxDelay = TimeSpan.FromMilliseconds(500);
        }), loggerFactory);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));

        long verified = 0, failures = 0, mismatches = 0;
        var mismatchDetails = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var workers = Enumerable.Range(0, 64).Select(worker => Task.Run(async () =>
        {
            var key = $"soak:{worker}";
            while (!stop.IsCancellationRequested)
            {
                var expected = $"{worker}:{Guid.NewGuid():N}";
                try
                {
                    if (!await client.Strings.SetAsync(key, expected))
                    {
                        continue;
                    }

                    var actual = (string?)await client.Strings.GetAsync(key);
                    if (actual == expected)
                    {
                        Interlocked.Increment(ref verified);
                    }
                    else
                    {
                        Interlocked.Increment(ref mismatches);
                        mismatchDetails.Enqueue($"worker {worker}: expected {expected}, got {actual}");
                    }
                }
                catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        })).ToList();

        var chaos = Task.Run(async () =>
        {
            var round = 0;
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                if (stop.IsCancellationRequested)
                {
                    break;
                }

                if (++round % 4 == 0)
                {
                    await server.RestartAsync(TimeSpan.FromMilliseconds(300));
                }
                else
                {
                    await server.CliAsync("CLIENT", "KILL", "TYPE", "normal");
                }
            }
        });

        await Task.WhenAll(workers);
        await chaos;
        output.WriteLine($"verified={verified} failures={failures} mismatches={mismatches} in {seconds}s");

        Assert.Empty(mismatchDetails);
        Assert.True(verified > 1000, $"only {verified} round trips verified");
        Assert.True(failures > 0, "the chaos did not cause any failure; the test is not exercising reconnects");

        // After the chaos the client recovers by itself (the last restart may have just happened).
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                Assert.Equal("PONG", (string?)await client.ExecuteAsync("PING"));
                break;
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    foreach (var entry in logs.Entries.TakeLast(40))
                    {
                        output.WriteLine($"{entry.Level}: {entry.Message} {entry.Exception?.GetType().Name}: {entry.Exception?.Message}");
                    }

                    throw;
                }
            }
        }
    }
}
