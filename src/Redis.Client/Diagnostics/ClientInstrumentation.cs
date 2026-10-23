using System.Diagnostics;
using System.Diagnostics.Metrics;
using Redis.Client.Connections;

namespace Redis.Client.Diagnostics;

/// <summary>
/// Metrics and tracing of one client instance. Instruments follow the OpenTelemetry database client
/// semantic conventions where they exist; Redis-specific ones use the <c>redis.client.</c> prefix.
/// </summary>
internal sealed class ClientInstrumentation : IDisposable
{
    public const string SourceName = "Redis.Client";

    private static readonly double[] DurationBuckets = [0.0001, 0.0005, 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10];

    private readonly Meter _meter;
    private readonly Histogram<double> _operationDuration;
    private readonly UpDownCounter<long> _connections;
    private readonly Counter<long> _reconnects;
    private readonly Counter<long> _timeouts;
    private readonly Histogram<double> _dedicatedPoolWait;
    private readonly Counter<long> _pubSubDropped;
    private readonly KeyValuePair<string, object?> _serverAddress;
    private readonly KeyValuePair<string, object?> _serverPort;
    private readonly KeyValuePair<string, object?> _namespace;

    public ClientInstrumentation(RedisClientOptions options, IMeterFactory? meterFactory, Func<long> backlogSize, Func<long> pendingSize)
    {
        var (address, port) = options.Endpoints.Count > 0 ? SplitEndpoint(options.Endpoints[0]) : (string.Empty, 0);
        ServerAddress = address;
        ServerPort = port;
        Namespace = options.Database.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RecordArguments = options.RecordCommandArguments;
        _serverAddress = new("server.address", address);
        _serverPort = new("server.port", port);
        _namespace = new("db.namespace", Namespace);

        _meter = meterFactory?.Create(SourceName) ?? new Meter(SourceName);
        _operationDuration = _meter.CreateHistogram<double>(
            "db.client.operation.duration",
            unit: "s",
            description: "Duration of Redis commands.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
        _connections = _meter.CreateUpDownCounter<long>("redis.client.connections", unit: "{connection}", description: "Open connections by kind.");
        _reconnects = _meter.CreateCounter<long>("redis.client.reconnects", unit: "{reconnect}", description: "Successful reconnects after a connection loss.");
        _timeouts = _meter.CreateCounter<long>("redis.client.timeouts", unit: "{command}", description: "Commands that exceeded their deadline.");
        _dedicatedPoolWait = _meter.CreateHistogram<double>(
            "redis.client.dedicated_pool.wait.duration",
            unit: "s",
            description: "Time spent waiting to lease a dedicated connection.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });
        _pubSubDropped = _meter.CreateCounter<long>("redis.client.pubsub.dropped", unit: "{message}", description: "Pub/Sub messages dropped because a subscription buffer was full.");
        _meter.CreateObservableGauge("redis.client.backlog.size", () => backlogSize(), unit: "{command}", description: "Commands waiting to be written.");
        _meter.CreateObservableGauge("redis.client.pending.size", () => pendingSize(), unit: "{command}", description: "Commands written and waiting for a reply.");
    }

    public static ActivitySource ActivitySource { get; } = new(SourceName, typeof(ClientInstrumentation).Assembly.GetName().Version?.ToString());

    public string ServerAddress { get; }

    public int ServerPort { get; }

    public string Namespace { get; }

    public bool RecordArguments { get; }

    public bool IsDurationEnabled => _operationDuration.Enabled;

    public void ConnectionOpened(ConnectionKind kind) => _connections.Add(1, KindTag(kind), _serverAddress, _serverPort);

    public void ConnectionClosed(ConnectionKind kind) => _connections.Add(-1, KindTag(kind), _serverAddress, _serverPort);

    public void Reconnected() => _reconnects.Add(1, _serverAddress, _serverPort);

    public void Timeout(string operation) => _timeouts.Add(1, new KeyValuePair<string, object?>("db.operation.name", operation), _serverAddress, _serverPort);

    public void DedicatedPoolWait(TimeSpan elapsed) => _dedicatedPoolWait.Record(elapsed.TotalSeconds, _serverAddress, _serverPort);

    public void PubSubDropped(string channel) => _pubSubDropped.Add(1, new KeyValuePair<string, object?>("messaging.destination.name", channel), _serverAddress, _serverPort);

    public void RecordDuration(string operation, TimeSpan elapsed, string? errorType)
    {
        var tags = new TagList
        {
            { "db.system.name", "redis" },
            { "db.operation.name", operation },
            _namespace,
            _serverAddress,
            _serverPort,
        };
        if (errorType is not null)
        {
            tags.Add("error.type", errorType);
        }

        _operationDuration.Record(elapsed.TotalSeconds, tags);
    }

    public void Dispose() => _meter.Dispose();

    private static KeyValuePair<string, object?> KindTag(ConnectionKind kind) => new("redis.connection.kind", kind switch
    {
        ConnectionKind.Multiplexed => "multiplexed",
        ConnectionKind.Dedicated => "dedicated",
        _ => "subscriber",
    });

    private static (string Address, int Port) SplitEndpoint(System.Net.EndPoint endpoint) => endpoint switch
    {
        System.Net.DnsEndPoint dns => (dns.Host, dns.Port),
        System.Net.IPEndPoint ip => (ip.Address.ToString(), ip.Port),
        _ => (endpoint.ToString() ?? string.Empty, 0),
    };
}
