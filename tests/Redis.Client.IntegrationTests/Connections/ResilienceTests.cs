using Redis.Client.IntegrationTests.Infrastructure;
using Redis.Client.Protocol;

namespace Redis.Client.IntegrationTests.Connections;

/// <summary>Timeouts and cancellation against a real server.</summary>
[Collection(RedisCollection.Name)]
public sealed class TimeoutIntegrationTests(RedisFixture fixture)
{
    [Fact]
    public async Task Late_reply_after_debug_sleep_is_not_delivered_to_next_command()
    {
        var key = RedisFixture.Prefix() + "k";
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o => o.CommandTimeout = TimeSpan.FromMilliseconds(500)));
        await router.StringAsync("SET", key, "value");

        var connection = router.Multiplexed[0].Connection;

        // 1.2 s: past the 0.5 s deadline, but answered before stall detection would replace the connection.
        var error = await Assert.ThrowsAsync<RedisTimeoutException>(() => router.StringAsync("DEBUG", "SLEEP", "1.2"));
        Assert.True(error.MayHaveExecuted);

        await Task.Delay(1200);
        Assert.Equal("value", (string?)await router.ValueAsync("GET", key));
        Assert.Same(connection, router.Multiplexed[0].Connection);
    }

    [Fact]
    public async Task Cancelling_a_sent_command_keeps_replies_in_sync()
    {
        var key = RedisFixture.Prefix() + "k";
        await using var router = TestRouter.Create(fixture.Server.CreateOptions());
        await router.StringAsync("SET", key, "value");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            router.ExecuteAsync(new RedisCommand(Commands.Lookup("DEBUG"), "SLEEP", "0.6"), f => f.GetString(), cancellation.Token));

        await Task.Delay(600);
        Assert.Equal("value", (string?)await router.ValueAsync("GET", key));
    }

    [Fact]
    public async Task Killed_connection_is_replaced_transparently()
    {
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o => o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(20)));
        var id = await router.IntegerAsync("CLIENT", "ID");
        await fixture.Server.CliAsync("CLIENT", "KILL", "ID", id.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Queue policy: a command issued right after the kill waits for the new connection.
        Assert.Equal("PONG", await router.StringAsync("PING").WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(id, await router.IntegerAsync("CLIENT", "ID"));
    }

    [Fact]
    public async Task Rotated_password_is_used_after_reconnect()
    {
        var user = "rot" + Guid.NewGuid().ToString("N")[..8];
        await fixture.Server.CliAsync("ACL", "SETUSER", user, "on", ">pw1", "~*", "&*", "+@all");
        var password = "pw1";
        await using var router = TestRouter.Create(fixture.Server.CreateOptions(o =>
        {
            o.Username = null;
            o.Password = null;
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(20);
            o.CredentialsProvider = _ => ValueTask.FromResult(new RedisCredentials(user, Volatile.Read(ref password)));
        }));
        Assert.Equal(user, await router.StringAsync("ACL", "WHOAMI"));

        await fixture.Server.CliAsync("ACL", "SETUSER", user, "resetpass", ">pw2");
        Volatile.Write(ref password, "pw2");
        await fixture.Server.CliAsync("CLIENT", "KILL", "USER", user);

        Assert.Equal(user, await router.StringAsync("ACL", "WHOAMI").WaitAsync(TimeSpan.FromSeconds(10)));
        await fixture.Server.CliAsync("ACL", "DELUSER", user);
    }
}

/// <summary>Scenarios that stop or freeze the server; each test owns its container.</summary>
public sealed class ResilienceTests
{
    [Fact]
    public async Task Client_reconnects_after_redis_restart()
    {
        await using var server = await RedisServer.StartAsync();
        await using var router = TestRouter.Create(server.CreateOptions(o =>
        {
            o.CommandTimeout = TimeSpan.FromSeconds(30);
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(50);
            o.ReconnectMaxDelay = TimeSpan.FromMilliseconds(500);
        }));
        Assert.Equal("PONG", await router.StringAsync("PING"));

        var restart = server.RestartAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(300);
        var duringOutage = router.StringAsync("PING");
        await restart;

        Assert.Equal("PONG", await duringOutage.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("PONG", await router.StringAsync("PING"));
    }

    [Fact]
    public async Task Frozen_connection_is_detected_by_keep_alive()
    {
        await using var server = await RedisServer.StartAsync();
        await using var router = TestRouter.Create(server.CreateOptions(o =>
        {
            o.KeepAliveInterval = TimeSpan.FromSeconds(1);
            o.CommandTimeout = TimeSpan.FromSeconds(1);
            o.ConnectTimeout = TimeSpan.FromSeconds(1);
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(50);
            o.ReconnectMaxDelay = TimeSpan.FromMilliseconds(500);
        }));
        await router.ConnectAsync(default);
        var before = router.Multiplexed[0].Connection!.Id;

        // A paused container keeps TCP connections open but never answers.
        await server.PauseAsync();
        await Task.Delay(TimeSpan.FromSeconds(4));
        await server.UnpauseAsync();

        var after = await WaitForNewConnectionAsync(router, before);
        Assert.NotEqual(before, after);
        Assert.Equal("PONG", await router.StringAsync("PING"));
    }

    private static async Task<long> WaitForNewConnectionAsync(Redis.Client.Connections.SingleNodeRouter router, long previous)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (router.Multiplexed[0].Connection is { } connection && connection.Id != previous)
            {
                return connection.Id;
            }

            await Task.Delay(50);
        }

        return previous;
    }
}
