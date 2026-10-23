using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Redis.Client.IntegrationTests.Infrastructure;

namespace Redis.Client.IntegrationTests;

public abstract class PubSubTestsBase(RedisFixture fixture, RedisProtocol protocol)
{
    protected RedisClient CreateClient(Action<RedisClientOptions>? configure = null, ILoggerFactory? loggerFactory = null) =>
        new(fixture.Server.CreateOptions(o =>
        {
            o.Protocol = protocol;
            configure?.Invoke(o);
        }), loggerFactory);

    protected static string Channel() => "ch:" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Channel_subscription_receives_published_messages()
    {
        var channel = Channel();
        await using var client = CreateClient();
        await using var subscription = await client.Subscriber.SubscribeAsync(channel);
        Assert.Equal(1, await client.Subscriber.PublishAsync(channel, "42"));

        var message = await subscription.Messages.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(channel, message.Channel);
        Assert.Equal("42", (string?)message.Value);
        Assert.Null(message.Pattern);
    }

    [Fact]
    public async Task Pattern_subscription_reports_channel_and_pattern()
    {
        var prefix = Channel();
        await using var client = CreateClient();
        await using var subscription = await client.Subscriber.SubscribePatternAsync(prefix + ".*");
        await client.Subscriber.PublishAsync(prefix + ".eu", "x");

        var message = await subscription.Messages.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(prefix + ".eu", message.Channel);
        Assert.Equal(prefix + ".*", message.Pattern);
    }

    [Fact]
    public async Task Publish_without_subscribers_returns_zero()
    {
        await using var client = CreateClient();
        Assert.Equal(0, await client.Subscriber.PublishAsync(Channel(), "x"));
    }

    [Fact]
    public async Task Local_subscribers_share_one_server_subscription()
    {
        var channel = Channel();
        await using var client = CreateClient();
        var first = await client.Subscriber.SubscribeAsync(channel);
        var second = await client.Subscriber.SubscribeAsync(channel);
        Assert.Equal("1", await fixture.Server.CliAsync("PUBSUB", "NUMSUB", channel).ContinueWith(t => t.Result.Split('\n')[1]));

        await client.Subscriber.PublishAsync(channel, "a");
        Assert.Equal("a", (string?)(await second.Messages.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Value);
        Assert.Equal("a", (string?)(await first.Messages.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Value);

        await first.DisposeAsync();
        Assert.Equal("1", (await fixture.Server.CliAsync("PUBSUB", "NUMSUB", channel)).Split('\n')[1]);
        await client.Subscriber.PublishAsync(channel, "b");
        Assert.Equal("b", (string?)(await second.Messages.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).Value);

        await second.DisposeAsync();
        Assert.Equal("0", (await fixture.Server.CliAsync("PUBSUB", "NUMSUB", channel)).Split('\n')[1]);
    }

    [Fact]
    public async Task Async_enumeration_and_handler_variant()
    {
        var channel = Channel();
        await using var client = CreateClient();
        var received = new List<string>();
        var three = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var handled = await client.Subscriber.SubscribeAsync(channel, (m, _) =>
        {
            lock (received)
            {
                received.Add((string)m.Value!);
                if (received.Count == 3)
                {
                    three.TrySetResult();
                }
            }

            return ValueTask.CompletedTask;
        });
        await using var enumerated = await client.Subscriber.SubscribeAsync(channel);

        foreach (var value in new[] { "1", "2", "3" })
        {
            await client.Subscriber.PublishAsync(channel, value);
        }

        await three.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["1", "2", "3"], received);

        var collected = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var message in enumerated.WithCancellation(timeout.Token))
        {
            collected.Add((string)message.Value!);
            if (collected.Count == 3)
            {
                break;
            }
        }

        Assert.Equal(["1", "2", "3"], collected);
    }

    [Fact]
    public async Task Handler_exception_does_not_stop_delivery()
    {
        var channel = Channel();
        var logs = new ListLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(logs));
        await using var client = CreateClient(loggerFactory: loggerFactory);
        var second = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var subscription = await client.Subscriber.SubscribeAsync(channel, (m, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("handler failure");
            }

            second.TrySetResult((string)m.Value!);
            return ValueTask.CompletedTask;
        });

        await client.Subscriber.PublishAsync(channel, "first");
        await client.Subscriber.PublishAsync(channel, "second");
        Assert.Equal("second", await second.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Error && e.Exception?.Message == "handler failure");
    }

    [Fact]
    public async Task Full_buffer_drops_oldest_and_counts_drops()
    {
        var channel = Channel();
        await using var client = CreateClient(o => o.SubscriptionBufferSize = 100);
        await using var slow = await client.Subscriber.SubscribeAsync(channel);
        await using var fast = await client.Subscriber.SubscribeAsync(channel);
        var fastCount = 0;
        var fastDone = Task.Run(async () =>
        {
            await foreach (var _ in fast)
            {
                if (++fastCount == 150)
                {
                    return;
                }
            }
        });

        for (var i = 0; i < 150; i++)
        {
            await client.Subscriber.PublishAsync(channel, i);
        }

        // The slow subscription never reads, yet the fast one gets everything.
        await fastDone.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => slow.DroppedMessages == 50);
        Assert.Equal(100, slow.Messages.Count);
        Assert.Equal(50, (long)(await slow.Messages.ReadAsync()).Value);
    }

    [Fact]
    public async Task Subscription_connection_is_separate_from_commands()
    {
        var channel = Channel();
        await using var client = CreateClient();
        await using var subscription = await client.Subscriber.SubscribeAsync(channel);
        using var stop = new CancellationTokenSource();
        var flood = Task.Run(async () =>
        {
            await using var publisher = CreateClient();
            while (!stop.IsCancellationRequested)
            {
                await publisher.Subscriber.PublishAsync(channel, new string('x', 1000));
            }
        });

        await Task.Delay(200);
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            await client.Strings.GetAsync("k");
        }

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"20 GETs took {stopwatch.Elapsed}");
        await stop.CancelAsync();
        await flood;
    }

    protected static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
            await Task.Delay(20);
        }
    }
}

[Collection(RedisCollection.Name)]
public sealed class PubSubResp2Tests(RedisFixture fixture) : PubSubTestsBase(fixture, RedisProtocol.Resp2);

[Collection(RedisCollection.Name)]
public sealed class PubSubResp3Tests(RedisFixture fixture) : PubSubTestsBase(fixture, RedisProtocol.Resp3);

public sealed class PubSubRecoveryTests
{
    [Fact]
    public async Task Subscriptions_are_restored_after_redis_restart()
    {
        await using var server = await RedisServer.StartAsync();
        await using var client = new RedisClient(server.CreateOptions(o =>
        {
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(50);
            o.ReconnectMaxDelay = TimeSpan.FromMilliseconds(500);
            o.CommandTimeout = TimeSpan.FromSeconds(10);
        }));
        await using var subscription = await client.Subscriber.SubscribeAsync("orders");
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        subscription.ConnectionLost += (_, _) => lost.TrySetResult();
        subscription.ConnectionRestored += (_, _) => restored.TrySetResult();

        await server.RestartAsync(TimeSpan.FromMilliseconds(500));
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, await client.Subscriber.PublishAsync("orders", "after-restart"));
        var message = await subscription.Messages.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("after-restart", (string?)message.Value);
    }
}

/// <summary>Captures log entries for assertions.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    public List<(LogLevel Level, string Category, string Message, Exception? Exception)> Entries { get; } = [];

    public ILogger CreateLogger(string categoryName) => new ListLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class ListLogger(ListLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (provider.Entries)
            {
                provider.Entries.Add((logLevel, category, formatter(state, exception), exception));
            }
        }
    }
}
