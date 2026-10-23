using Redis.Client.IntegrationTests.Infrastructure;
using Redis.Client.Protocol;

namespace Redis.Client.IntegrationTests.Connections;

[Collection(RedisCollection.Name)]
public sealed class MultiplexerTests(RedisFixture fixture)
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Concurrent_callers_get_their_own_replies(int connections)
    {
        var name = $"mux-{connections}-{Guid.NewGuid():N}"[..20];
        var prefix = RedisFixture.Prefix();
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o =>
        {
            o.MultiplexedConnectionCount = connections;
            o.ClientName = name;
            o.CommandTimeout = TimeSpan.FromSeconds(30);
        }));

        const int Callers = 10_000;
        var results = await Task.WhenAll(Enumerable.Range(0, Callers).Select(async i =>
        {
            var key = $"{prefix}{i}";
            var incremented = await router.IntegerAsync("INCRBY", key, i);
            var read = await router.ValueAsync("GET", key);
            return (i, incremented, read: (long)read);
        }));

        Assert.All(results, r =>
        {
            Assert.Equal(r.i, r.incremented);
            Assert.Equal(r.i, r.read);
        });
        Assert.Equal(connections, await TestRouter.CountConnectionsAsync(fixture.Server, name));
        Assert.Equal(connections, router.Multiplexed.Count);
    }

    [Fact]
    public async Task Server_error_does_not_break_the_connection()
    {
        var key = RedisFixture.Prefix() + "string";
        await using var router = TestRouter.Create(fixture.Server.CreateOptions());
        await router.StringAsync("SET", key, "v");
        var error = await Assert.ThrowsAsync<RedisServerException>(() => router.IntegerAsync("LPUSH", key, "x"));
        Assert.Equal("WRONGTYPE", error.ErrorCode);
        Assert.Equal("v", (string?)await router.ValueAsync("GET", key));
    }

    [Fact]
    public async Task Push_frames_between_replies_go_to_the_push_handler()
    {
        var key = RedisFixture.Prefix() + "tracked";
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o => o.Protocol = RedisProtocol.Resp3));
        var pushes = new List<string>();
        var invalidated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        router.Multiplexed[0].PushHandler = frame =>
        {
            var first = true;
            foreach (var child in frame.GetChildren())
            {
                if (first)
                {
                    lock (pushes)
                    {
                        pushes.Add(child.GetString());
                    }

                    first = false;
                }
            }

            invalidated.TrySetResult();
        };

        await router.StringAsync("SET", key, "1");
        Assert.Equal("OK", await router.StringAsync("CLIENT", "TRACKING", "ON"));
        Assert.Equal("1", (string?)await router.ValueAsync("GET", key));

        // Another connection modifies the tracked key: the server pushes an invalidation to ours.
        await fixture.Server.CliAsync("SET", key, "2");
        await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("2", (string?)await router.ValueAsync("GET", key));
        Assert.Equal(1, await router.IntegerAsync("EXISTS", key));
        Assert.Contains("invalidate", pushes);
    }

    [Fact]
    public async Task Large_values_survive_multiplexing()
    {
        var key = RedisFixture.Prefix() + "large";
        var payload = new byte[5 * 1024 * 1024];
        Random.Shared.NextBytes(payload);
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o => o.CommandTimeout = TimeSpan.FromSeconds(30)));
        await router.StringAsync("SET", key, payload);
        var small = Task.WhenAll(Enumerable.Range(0, 100).Select(_ => router.StringAsync("PING")));
        var read = await router.ValueAsync("GET", key);
        await small;
        Assert.Equal(payload, (byte[]?)read);
    }

    [Fact]
    public async Task Huge_aggregate_reply_is_parsed()
    {
        var key = RedisFixture.Prefix() + "hash";
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o => o.CommandTimeout = TimeSpan.FromSeconds(60)));
        const int Fields = 200_000;
        foreach (var chunk in Enumerable.Range(0, Fields).Chunk(10_000))
        {
            var args = new List<RedisValue> { key };
            foreach (var i in chunk)
            {
                args.Add($"field{i}");
                args.Add(i);
            }

            await router.IntegerAsync("HSET", [.. args]);
        }

        var count = await router.ExecuteAsync(new RedisCommand(Commands.HGetAll, key), f => f.ChildCount, default);
        Assert.Equal(Fields * 2, count);
    }
}
