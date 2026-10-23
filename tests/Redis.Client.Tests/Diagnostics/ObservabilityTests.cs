using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Redis.Client.Protocol;
using Redis.Client.Tests.Fakes;
using static Redis.Client.Tests.Connections.MultiplexedConnectionTests;

namespace Redis.Client.Tests.Diagnostics;

public sealed class ObservabilityTests
{
    [Fact]
    public async Task Command_creates_client_activity_with_semantic_tags()
    {
        await using var server = new FakeRedisServer { Handler = _ => "$1\r\nv\r\n" };
        using var capture = new ActivityCapture(server.Port);
        await using var client = new RedisClient(server.CreateOptions(o => o.Database = 2));

        await client.Strings.GetAsync("k");

        var activity = Assert.Single(capture.Activities, a => a.OperationName == "GET");
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("redis", activity.GetTagItem("db.system.name"));
        Assert.Equal("GET", activity.GetTagItem("db.operation.name"));
        Assert.Equal("2", activity.GetTagItem("db.namespace"));
        Assert.Equal("127.0.0.1", activity.GetTagItem("server.address"));
        Assert.Equal(server.Port, activity.GetTagItem("server.port"));
        Assert.Null(activity.GetTagItem("db.query.text"));
    }

    [Fact]
    public async Task Server_error_marks_activity_as_error()
    {
        await using var server = new FakeRedisServer { Handler = _ => "-WRONGTYPE Operation against a key holding the wrong kind of value\r\n" };
        using var capture = new ActivityCapture(server.Port);
        await using var client = new RedisClient(server.CreateOptions());

        await Assert.ThrowsAsync<RedisServerException>(() => client.Lists.LeftPushAsync("k", "v").AsTask());

        var activity = Assert.Single(capture.Activities, a => a.OperationName == "LPUSH");
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("WRONGTYPE", activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task Command_activity_is_child_of_the_current_activity()
    {
        await using var server = new FakeRedisServer { Handler = _ => "$1\r\nv\r\n" };
        using var capture = new ActivityCapture(server.Port);
        using var parentSource = new ActivitySource("Redis.Client.Tests.Parent");
        using var parentListener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == parentSource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(parentListener);
        await using var client = new RedisClient(server.CreateOptions());

        using (var parent = parentSource.StartActivity("http-request"))
        {
            await client.Strings.GetAsync("k");
            var child = Assert.Single(capture.Activities, a => a.OperationName == "GET");
            Assert.Equal(parent!.TraceId, child.TraceId);
            Assert.Equal(parent.SpanId, child.ParentSpanId);
        }
    }

    [Fact]
    public async Task Without_listeners_no_activity_is_created()
    {
        await using var server = new FakeRedisServer { Handler = _ => "$1\r\nv\r\n" };
        await using var client = new RedisClient(server.CreateOptions());
        Assert.Null(Activity.Current);
        await client.Strings.GetAsync("k");
        Assert.Null(Activity.Current);
    }

    [Fact]
    public async Task Arguments_are_recorded_only_when_enabled()
    {
        await using var server = new FakeRedisServer();
        using var capture = new ActivityCapture(server.Port);

        await using (var client = new RedisClient(server.CreateOptions()))
        {
            await client.Strings.SetAsync("user:1", "secret-value");
        }

        Assert.DoesNotContain(capture.Activities.SelectMany(a => a.TagObjects), t => t.Value?.ToString()?.Contains("secret-value", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(capture.Activities.SelectMany(a => a.TagObjects), t => t.Value?.ToString()?.Contains("user:1", StringComparison.Ordinal) == true);

        await using (var client = new RedisClient(server.CreateOptions(o => o.RecordCommandArguments = true)))
        {
            await client.Strings.SetAsync("user:1", "v");
        }

        Assert.Contains(capture.Activities, a => (string?)a.GetTagItem("db.query.text") == "SET user:1 v");
    }

    [Fact]
    public void Credentials_are_always_masked_in_command_text()
    {
        Assert.Equal("AUTH ? ?", RedisClient.FormatCommand(new RedisCommand(Commands.Auth, "app", "TopSecret1")));
        Assert.Equal("HELLO 3 ? ? ? ? ?", RedisClient.FormatCommand(new RedisCommand(Commands.Hello, 3, "AUTH", "app", "TopSecret1", "SETNAME", "n")));
    }

    [Fact]
    public async Task Password_and_values_never_reach_logs_traces_or_errors()
    {
        await using var server = new FakeRedisServer { Password = "TopSecret1" };
        using var capture = new ActivityCapture(server.Port);
        var logs = new ListLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));

        await using (var client = new RedisClient(server.CreateOptions(o => o.Password = "TopSecret1"), loggerFactory))
        {
            await client.Strings.SetAsync("user:1", "secret-value");
        }

        var wrong = new RedisClient(server.CreateOptions(o => o.Password = "WrongSecret2"), loggerFactory);
        var error = await Assert.ThrowsAsync<RedisAuthenticationException>(() => wrong.ConnectAsync().AsTask());
        await wrong.DisposeAsync();

        var everything = string.Join('\n',
            logs.Entries.Select(e => e.Message + e.Exception)
                .Concat(capture.Activities.SelectMany(a => a.TagObjects.Select(t => t.Value?.ToString())))
                .Append(error.ToString()));
        Assert.DoesNotContain("TopSecret1", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("WrongSecret2", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", everything, StringComparison.Ordinal);
        Assert.NotEmpty(logs.Entries);
    }

    [Fact]
    public async Task Connection_lifecycle_is_logged()
    {
        await using var server = new FakeRedisServer();
        var logs = new ListLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(logs));
        await using var client = new RedisClient(server.CreateOptions(o => o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10)), loggerFactory);
        await client.ConnectAsync();

        server.DropConnections();
        await WaitUntilAsync(() => logs.Entries.Any(e => e.Message.Contains("restored", StringComparison.Ordinal)));

        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("established", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("lost", StringComparison.Ordinal) && e.Message.Contains($":{server.Port}", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("restored", StringComparison.Ordinal));
        Assert.All(logs.Entries, e => Assert.Equal("Redis.Client", e.Category));
    }

    [Fact]
    public async Task Metrics_are_published()
    {
        await using var server = new FakeRedisServer { Handler = c => c.Key == "slow" ? null : "$1\r\nv\r\n" };
        using var metrics = new MetricCapture(server.Port);
        await using var client = new RedisClient(server.CreateOptions(o =>
        {
            o.CommandTimeout = TimeSpan.FromMilliseconds(200);
            o.ReconnectBaseDelay = TimeSpan.FromMilliseconds(10);
        }));

        for (var i = 0; i < 10; i++)
        {
            await client.Strings.GetAsync("k");
        }

        await Assert.ThrowsAsync<RedisTimeoutException>(() => client.Strings.GetAsync("slow").AsTask());
        server.DropConnections();
        await WaitUntilAsync(() => metrics.Sum("redis.client.reconnects") >= 1);
        metrics.RecordObservables();

        Assert.Equal(10, metrics.Count("db.client.operation.duration", ("db.operation.name", "GET"), ("error.type", null)));
        Assert.Equal(1, metrics.Count("db.client.operation.duration", ("error.type", typeof(RedisTimeoutException).FullName)));
        Assert.Equal(1, metrics.Sum("redis.client.timeouts"));
        Assert.Equal(1, metrics.Sum("redis.client.reconnects"));
        Assert.Equal(1, metrics.Sum("redis.client.connections", ("redis.connection.kind", "multiplexed")));
        Assert.True(metrics.Has("redis.client.backlog.size"));
        Assert.True(metrics.Has("redis.client.pending.size"));
    }

    /// <summary>Collects activities of this library for one fake server (listeners are process-wide).</summary>
    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly ConcurrentQueue<Activity> _activities = new();

        public ActivityCapture(int port)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == "Redis.Client",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = a =>
                {
                    if (a.GetTagItem("server.port") is int p && p == port)
                    {
                        _activities.Enqueue(a);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyCollection<Activity> Activities => [.. _activities];

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>Collects measurements of this library for one fake server.</summary>
    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = new();
        private readonly int _port;

        public MetricCapture(int port)
        {
            _port = port;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Redis.Client")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
            _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
            _listener.Start();
        }

        public void RecordObservables() => _listener.RecordObservableInstruments();

        public bool Has(string instrument) => _measurements.Any(m => m.Instrument == instrument);

        public int Count(string instrument, params (string Key, string? Value)[] tags) =>
            Matching(instrument, tags).Count();

        public double Sum(string instrument, params (string Key, string? Value)[] tags) =>
            Matching(instrument, tags).Sum(m => m.Value);

        public void Dispose() => _listener.Dispose();

        private IEnumerable<(string Instrument, double Value, Dictionary<string, object?> Tags)> Matching(string instrument, (string Key, string? Value)[] tags) =>
            _measurements.Where(m => m.Instrument == instrument && tags.All(t => t.Value is null
                ? !m.Tags.ContainsKey(t.Key)
                : m.Tags.TryGetValue(t.Key, out var v) && v?.ToString() == t.Value));

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }

            // Observable gauges carry no tags; everything else must belong to this test's server.
            if (dictionary.TryGetValue("server.port", out var port) ? port is int p && p == _port : instrument.Name.EndsWith(".size", StringComparison.Ordinal))
            {
                _measurements.Enqueue((instrument.Name, value, dictionary));
            }
        }
    }
}

/// <summary>Captures log entries for assertions.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Category, string Message, Exception? Exception)> _entries = new();

    public IReadOnlyCollection<(LogLevel Level, string Category, string Message, Exception? Exception)> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new ListLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class ListLogger(ListLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider._entries.Enqueue((logLevel, category, formatter(state, exception), exception));
    }
}
