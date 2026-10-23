using Microsoft.Extensions.Logging.Abstractions;
using Redis.Client.Connections;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;
using Redis.Client.Tests.Fakes;

namespace Redis.Client.Tests.Connections;

public sealed class MultiplexedConnectionTests
{
    [Fact]
    public async Task Protocol_error_fails_waiting_commands_and_reconnects()
    {
        await using var server = new FakeRedisServer
        {
            Handler = c => c.Key == "bad" ? "@garbage\r\n" : "$2\r\nok\r\n",
        };
        await using var router = CreateRouter(server.CreateOptions(o => o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10)));
        await router.ConnectAsync(default);

        var bad = Get(router, "bad");
        var error = await Assert.ThrowsAsync<RedisConnectionException>(() => bad);
        Assert.True(error.MayHaveExecuted);
        Assert.IsType<RedisProtocolException>(error.InnerException);

        // The client reconnects by itself and later commands work.
        Assert.Equal("ok", (string?)await Get(router, "good").WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(server.Commands("HELLO").Count() >= 2);
    }

    [Fact]
    public async Task Unsolicited_reply_is_a_protocol_error()
    {
        await using var server = new FakeRedisServer { Handler = _ => "+OK\r\n+EXTRA\r\n" };
        await using var router = CreateRouter(server.CreateOptions(o => o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10)));
        await router.ConnectAsync(default);

        // The first command gets +OK; +EXTRA arrives with nobody waiting and the connection is dropped.
        Assert.Equal("OK", await router.ExecuteAsync(new RedisCommand(Commands.Set, "k", "v"), f => f.GetString(), default));
        await WaitUntilAsync(() => server.Commands("HELLO").Count() >= 2);
    }

    [Fact]
    public async Task Replies_are_matched_in_order_under_concurrency()
    {
        await using var server = new FakeRedisServer
        {
            Handler = c => $"${c.Key.Length}\r\n{c.Key}\r\n",
        };
        await using var router = CreateRouter(server.CreateOptions());
        var results = await Task.WhenAll(Enumerable.Range(0, 5000).Select(async i => (i, value: (string?)await Get(router, $"v{i}"))));
        Assert.All(results, r => Assert.Equal($"v{r.i}", r.value));
    }

    internal static SingleNodeRouter CreateRouter(RedisClientOptions options) =>
        new(options, NullLogger.Instance, new ClientInstrumentation(options, null, () => 0, () => 0));

    internal static Task<RedisValue> Get(ICommandRouter router, string key, CancellationToken cancellationToken = default) =>
        router.ExecuteAsync(new RedisCommand(Commands.Get, key), f => f.ToRedisValue(), cancellationToken);

    internal static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(10);
        }
    }
}
