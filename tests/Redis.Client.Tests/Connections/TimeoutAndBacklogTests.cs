using System.Net;
using Redis.Client.Protocol;
using Redis.Client.Tests.Fakes;
using static Redis.Client.Tests.Connections.MultiplexedConnectionTests;

namespace Redis.Client.Tests.Connections;

public sealed class TimeoutAndBacklogTests
{
    [Fact]
    public async Task Late_reply_of_timed_out_command_is_discarded()
    {
        await using var server = new FakeRedisServer
        {
            Handler = c => $"${c.Key.Length}\r\n{c.Key}\r\n",
            // Longer than the command timeout but shorter than the stall detection (2 x timeout + PING timeout),
            // so the connection survives and the late reply must be discarded on it.
            ReplyDelay = c => c.Key == "slow" ? TimeSpan.FromMilliseconds(450) : TimeSpan.Zero,
        };
        await using var router = CreateRouter(server.CreateOptions(o => o.CommandTimeout = TimeSpan.FromMilliseconds(200)));
        await router.ConnectAsync(default);
        var connection = router.Multiplexed[0].Connection;

        var error = await Assert.ThrowsAsync<RedisTimeoutException>(() => Get(router, "slow"));
        Assert.True(error.MayHaveExecuted);

        await Task.Delay(500);
        Assert.Equal("next", (string?)await Get(router, "next"));
        Assert.Same(connection, router.Multiplexed[0].Connection);
    }

    [Fact]
    public async Task Stalled_connection_is_replaced_quickly()
    {
        // The server stops answering but keeps the TCP connection open (half-open peer).
        var silent = false;
        await using var server = new FakeRedisServer { Handler = _ => Volatile.Read(ref silent) ? null : "+OK\r\n" };
        await using var router = CreateRouter(server.CreateOptions(o =>
        {
            o.CommandTimeout = TimeSpan.FromMilliseconds(200);
            o.KeepAliveInterval = TimeSpan.FromMinutes(10);
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10);
        }));
        await router.ConnectAsync(default);
        var stalled = router.Multiplexed[0].Connection!;

        Volatile.Write(ref silent, true);
        await Assert.ThrowsAsync<RedisTimeoutException>(() => router.ExecuteAsync(new RedisCommand(Commands.Set, "k", "v"), f => f.GetString(), default));
        await WaitUntilAsync(() => router.Multiplexed[0].Connection is { } c && c != stalled, timeoutMs: 3000);

        Volatile.Write(ref silent, false);
        Assert.Equal("OK", await router.ExecuteAsync(new RedisCommand(Commands.Set, "k", "v"), f => f.GetString(), default));
    }

    [Fact]
    public async Task Command_that_cannot_be_sent_times_out_without_executing()
    {
        var options = new RedisClientOptions
        {
            CommandTimeout = TimeSpan.FromMilliseconds(200),
            ConnectTimeout = TimeSpan.FromMilliseconds(200),
            ReconnectBaseDelay = TimeSpan.FromMilliseconds(50),
        };
        options.Endpoints.Add(new IPEndPoint(IPAddress.Loopback, GetClosedPort()));
        await using var router = CreateRouter(options);

        var error = await Assert.ThrowsAsync<RedisTimeoutException>(() => Get(router, "k"));
        Assert.False(error.MayHaveExecuted);
    }

    [Fact]
    public async Task Cancelled_before_sending_is_never_sent()
    {
        await using var server = new FakeRedisServer { HandshakeGate = new TaskCompletionSource(), Handler = _ => "$1\r\nv\r\n" };
        await using var router = CreateRouter(server.CreateOptions(o => o.CommandTimeout = TimeSpan.FromSeconds(10)));
        using var cancellation = new CancellationTokenSource();

        var cancelled = Get(router, "cancelled", cancellation.Token);
        var kept = Get(router, "kept");
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        server.HandshakeGate!.SetResult();
        Assert.Equal("v", (string?)await kept);
        Assert.DoesNotContain(server.Commands("GET"), c => c.Key == "cancelled");
        Assert.Contains(server.Commands("GET"), c => c.Key == "kept");
    }

    [Fact]
    public async Task Cancelled_after_sending_does_not_shift_replies()
    {
        await using var server = new FakeRedisServer
        {
            Handler = c => $"${c.Key.Length}\r\n{c.Key}\r\n",
            ReplyDelay = c => c.Key == "slow" ? TimeSpan.FromMilliseconds(300) : TimeSpan.Zero,
        };
        await using var router = CreateRouter(server.CreateOptions());
        await router.ConnectAsync(default);
        using var cancellation = new CancellationTokenSource();

        var slow = Get(router, "slow", cancellation.Token);
        await WaitUntilAsync(() => server.Commands("GET").Any());
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow);

        Assert.Equal("after", (string?)await Get(router, "after"));
    }

    [Fact]
    public async Task Already_cancelled_token_fails_immediately()
    {
        await using var server = new FakeRedisServer();
        await using var router = CreateRouter(server.CreateOptions());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Get(router, "k", new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Queue_policy_sends_backlog_after_reconnect()
    {
        await using var server = new FakeRedisServer { Handler = _ => "+OK\r\n" };
        await using var router = CreateRouter(server.CreateOptions(o =>
        {
            o.BacklogPolicy = BacklogPolicy.Queue;
            o.CommandTimeout = TimeSpan.FromSeconds(10);
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10);
        }));
        await router.ConnectAsync(default);

        server.HandshakeGate = new TaskCompletionSource();
        server.DropConnections();
        await WaitUntilAsync(() => router.Multiplexed[0].State == Redis.Client.Connections.ConnectionState.Reconnecting
                                   || server.Commands("HELLO").Count() >= 2);

        var set = router.ExecuteAsync(new RedisCommand(Commands.Set, "k", "v"), f => f.GetString(), default);
        await Task.Delay(100);
        Assert.False(set.IsCompleted);

        server.HandshakeGate.SetResult();
        Assert.Equal("OK", await set.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Fail_fast_policy_rejects_commands_while_disconnected()
    {
        await using var server = new FakeRedisServer { Handler = _ => "+OK\r\n" };
        await using var router = CreateRouter(server.CreateOptions(o =>
        {
            o.BacklogPolicy = BacklogPolicy.FailFast;
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10);
        }));
        await router.ConnectAsync(default);

        server.HandshakeGate = new TaskCompletionSource();
        server.DropConnections();
        await WaitUntilAsync(() => router.Multiplexed[0].Connection is null);

        var error = await Assert.ThrowsAsync<RedisConnectionException>(() => router.ExecuteAsync(new RedisCommand(Commands.Set, "k", "v"), f => f.GetString(), default));
        Assert.False(error.MayHaveExecuted);
        server.HandshakeGate.SetResult();
    }

    [Fact]
    public async Task Connection_loss_fails_in_flight_commands_without_retry()
    {
        await using var server = new FakeRedisServer { Handler = _ => null };
        await using var router = CreateRouter(server.CreateOptions(o => o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10)));
        await router.ConnectAsync(default);

        var inFlight = Get(router, "k");
        await WaitUntilAsync(() => server.Commands("GET").Any());
        server.DropConnections();

        var error = await Assert.ThrowsAsync<RedisConnectionException>(() => inFlight);
        Assert.True(error.MayHaveExecuted);
        await WaitUntilAsync(() => router.Multiplexed[0].Connection is not null);
        await Task.Delay(100);
        Assert.Single(server.Commands("GET"));
    }

    private static int GetClosedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
