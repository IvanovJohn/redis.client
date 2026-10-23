using System.Diagnostics;
using Redis.Client.IntegrationTests.Infrastructure;

namespace Redis.Client.IntegrationTests.Connections;

[Collection(RedisCollection.Name)]
public sealed class DedicatedPoolTests(RedisFixture fixture)
{
    [Fact]
    public async Task Blocking_command_does_not_delay_other_commands()
    {
        var prefix = RedisFixture.Prefix();
        var name = "blk" + Guid.NewGuid().ToString("N")[..8];
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o => o.ClientName = name));
        await router.ConnectAsync(default);
        var muxId = await router.IntegerAsync("CLIENT", "ID");

        var blpop = router.ValueAsync("BLPOP", prefix + "empty", 3);
        await Task.Delay(200);

        var stopwatch = Stopwatch.StartNew();
        Assert.Equal("PONG", await router.StringAsync("PING"));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"PING took {stopwatch.Elapsed}");

        // BLPOP (sent through the generic path) runs on another connection of this client.
        var clients = await fixture.Server.CliAsync("CLIENT", "LIST");
        var blocked = clients.Split('\n').Single(l => l.Contains($" name={name} ", StringComparison.Ordinal) && l.Contains("cmd=blpop", StringComparison.Ordinal));
        Assert.DoesNotContain($"id={muxId} ", blocked, StringComparison.Ordinal);

        Assert.True((await blpop).IsNull);
    }

    [Fact]
    public async Task Blocking_wait_longer_than_command_timeout_is_not_cut_short()
    {
        var key = RedisFixture.Prefix() + "queue";
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o => o.CommandTimeout = TimeSpan.FromSeconds(1)));

        var blpop = router.ExecuteAsync(
            new Redis.Client.Protocol.RedisCommand(Redis.Client.Protocol.Commands.BLPop, key, 4),
            f => f.ToArray().Select(c => c.ToRedisValue().ToString()).ToArray(),
            default);
        await Task.Delay(2000);
        await fixture.Server.CliAsync("RPUSH", key, "item");

        Assert.Equal([key, "item"], await blpop);
    }

    [Fact]
    public async Task Exhausted_pool_times_out_without_sending()
    {
        var prefix = RedisFixture.Prefix();
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o =>
        {
            o.DedicatedPoolSize = 2;

            // Redis answers a blocking timeout up to ~200 ms late (timer granularity); keep a margin above that.
            o.CommandTimeout = TimeSpan.FromSeconds(1);
        }));

        var first = router.ValueAsync("BLPOP", prefix + "a", 3);
        var second = router.ValueAsync("BLPOP", prefix + "b", 3);
        await Task.Delay(300);

        var error = await Assert.ThrowsAsync<RedisTimeoutException>(() => router.ValueAsync("BLPOP", prefix + "c", 3));
        Assert.False(error.MayHaveExecuted);
        Assert.Equal(2, router.Dedicated.InUse);

        await Task.WhenAll(first, second);
        Assert.Equal(0, router.Dedicated.InUse);
    }

    [Fact]
    public async Task Dedicated_connections_are_reused()
    {
        var prefix = RedisFixture.Prefix();
        var name = "reuse" + Guid.NewGuid().ToString("N")[..8];
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o => o.ClientName = name));
        for (var i = 0; i < 5; i++)
        {
            Assert.True((await router.ValueAsync("BLPOP", prefix + "empty", 0.1)).IsNull);
        }

        // One multiplexed connection is not opened here (no ordinary command was sent); one dedicated connection is reused.
        Assert.Equal(1, await TestRouter.CountConnectionsAsync(fixture.Server, name));
    }
}
