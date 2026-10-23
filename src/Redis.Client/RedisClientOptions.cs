using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Text;

namespace Redis.Client;

/// <summary>Version of the RESP protocol used on connections.</summary>
public enum RedisProtocol
{
    /// <summary>Try RESP3 (<c>HELLO 3</c>) and fall back to RESP2 if the server or a proxy rejects <c>HELLO</c>.</summary>
    Auto = 0,

    /// <summary>Always use RESP2; <c>HELLO</c> is not sent.</summary>
    Resp2 = 2,

    /// <summary>Always use RESP3; failure of <c>HELLO 3</c> is a connection error.</summary>
    Resp3 = 3,
}

/// <summary>What happens to commands issued while the connection is down.</summary>
public enum BacklogPolicy
{
    /// <summary>Keep the command until the connection is restored or the command deadline expires.</summary>
    Queue = 0,

    /// <summary>Fail the command immediately with <see cref="RedisConnectionException"/>.</summary>
    FailFast = 1,
}

/// <summary>What happens when a subscription buffer is full.</summary>
public enum SubscriptionOverflowPolicy
{
    /// <summary>Drop the oldest buffered message; other subscriptions are not affected.</summary>
    DropOldest = 0,

    /// <summary>Stop reading from the subscription connection until the consumer catches up.</summary>
    Wait = 1,
}

/// <summary>Credentials returned by <see cref="RedisClientOptions.CredentialsProvider"/>.</summary>
/// <param name="username">ACL user name, or <see langword="null"/> for the default user.</param>
/// <param name="password">The password.</param>
public sealed class RedisCredentials(string? username, string password)
{
    /// <summary>Gets the ACL user name, or <see langword="null"/> for the default user.</summary>
    public string? Username { get; } = username;

    /// <summary>Gets the password.</summary>
    public string Password { get; } = password ?? throw new ArgumentNullException(nameof(password));

    /// <summary>Returns the user name with the password masked.</summary>
    /// <returns>A string safe for logs.</returns>
    public override string ToString() => $"{Username ?? "default"}:***";
}

/// <summary>Settings of a <see cref="RedisClient"/>.</summary>
/// <remarks>
/// The client takes a snapshot of the options when it is created; later changes do not affect it.
/// Use <see cref="FromConnectionString"/> to parse a <c>redis://</c> or <c>rediss://</c> URI.
/// </remarks>
public sealed class RedisClientOptions
{
    /// <summary>Default Redis port.</summary>
    public const int DefaultPort = 6379;

    /// <summary>Gets the server endpoints. Exactly one endpoint is supported in this version.</summary>
    public IList<EndPoint> Endpoints { get; } = new List<EndPoint>();

    /// <summary>Gets or sets the ACL user name; requires <see cref="Password"/>.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the password (of <see cref="Username"/> or of the default user).</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets a callback that returns credentials on every connect and reconnect, e.g. from a secret store,
    /// so that rotated passwords are picked up without restarting. Cannot be combined with <see cref="Password"/>.
    /// </summary>
    public Func<CancellationToken, ValueTask<RedisCredentials>>? CredentialsProvider { get; set; }

    /// <summary>Gets or sets the database number selected on every connection. Default: 0.</summary>
    public int Database { get; set; }

    /// <summary>Gets or sets the connection name shown by <c>CLIENT LIST</c>.</summary>
    public string? ClientName { get; set; }

    /// <summary>Gets or sets the RESP protocol version. Default: <see cref="RedisProtocol.Auto"/>.</summary>
    public RedisProtocol Protocol { get; set; }

    /// <summary>Gets or sets a value indicating whether connections use TLS.</summary>
    public bool Ssl { get; set; }

    /// <summary>
    /// Gets or sets TLS settings: certificate validation callback, client certificates, target host (SNI).
    /// Used only when <see cref="Ssl"/> is enabled. Disabling certificate validation requires an explicit callback.
    /// </summary>
    public SslClientAuthenticationOptions? SslOptions { get; set; }

    /// <summary>Gets or sets the time allowed for TCP connect, TLS and the handshake. Default: 5 seconds.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the deadline of every command. Default: 5 seconds.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets after how much time without replies a <c>PING</c> checks the connection. Default: 60 seconds.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Gets or sets the first reconnect delay; it grows exponentially with jitter. Default: 100 ms.</summary>
    public TimeSpan ReconnectBaseDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets or sets the maximum reconnect delay. Default: 30 seconds.</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the handling of commands issued while disconnected. Default: <see cref="BacklogPolicy.Queue"/>.</summary>
    public BacklogPolicy BacklogPolicy { get; set; }

    /// <summary>Gets or sets the number of shared multiplexed connections. Default: 1.</summary>
    public int MultiplexedConnectionCount { get; set; } = 1;

    /// <summary>Gets or sets the maximum number of dedicated connections for blocking commands and WATCH transactions. Default: 16.</summary>
    public int DedicatedPoolSize { get; set; } = 16;

    /// <summary>Gets or sets the maximum size of a bulk reply and element count of an aggregate reply. Default: 512 MB.</summary>
    public long MaxBulkLength { get; set; } = 512L * 1024 * 1024;

    /// <summary>Gets or sets the maximum nesting depth of replies. Default: 64.</summary>
    public int MaxNestingDepth { get; set; } = 64;

    /// <summary>Gets or sets the number of messages buffered per subscription. Default: 1024.</summary>
    public int SubscriptionBufferSize { get; set; } = 1024;

    /// <summary>Gets or sets the behavior when a subscription buffer is full. Default: <see cref="SubscriptionOverflowPolicy.DropOldest"/>.</summary>
    public SubscriptionOverflowPolicy SubscriptionOverflowPolicy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether command text with arguments is recorded in traces (<c>db.query.text</c>).
    /// Off by default because arguments may contain personal data; credentials are always masked.
    /// </summary>
    public bool RecordCommandArguments { get; set; }

    /// <summary>Parses a connection string.</summary>
    /// <param name="connectionString">
    /// <c>redis://[[username]:password@]host[:port][/database][?option=value&amp;...]</c>, or <c>rediss://</c> for TLS.
    /// Option names match the properties of this class (case-insensitive). Time values are either a
    /// <see cref="TimeSpan"/> (<c>00:00:02</c>) or a number of milliseconds.
    /// </param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="FormatException">The string is not valid. The message never contains the password.</exception>
    public static RedisClientOptions FromConnectionString(string connectionString)
    {
        var options = new RedisClientOptions();
        options.ApplyConnectionString(connectionString);
        return options;
    }

    /// <summary>Applies the settings of a connection string to this instance (see <see cref="FromConnectionString"/>).</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <exception cref="FormatException">The string is not valid. The message never contains the password.</exception>
    public void ApplyConnectionString(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        ConnectionStringParser.Apply(connectionString, this);
    }

    /// <summary>Returns the settings with the password masked.</summary>
    /// <returns>A string safe for logs.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("Endpoints=").Append(string.Join(",", Endpoints.Select(FormatEndpoint)));
        if (Username is not null)
        {
            builder.Append(";Username=").Append(Username);
        }

        if (Password is not null)
        {
            builder.Append(";Password=***");
        }

        if (CredentialsProvider is not null)
        {
            builder.Append(";CredentialsProvider=(set)");
        }

        builder.Append(CultureInfo.InvariantCulture, $";Database={Database}");
        if (ClientName is not null)
        {
            builder.Append(";ClientName=").Append(ClientName);
        }

        builder.Append(CultureInfo.InvariantCulture, $";Protocol={Protocol};Ssl={Ssl}");
        builder.Append(CultureInfo.InvariantCulture, $";ConnectTimeout={ConnectTimeout};CommandTimeout={CommandTimeout};KeepAliveInterval={KeepAliveInterval}");
        builder.Append(CultureInfo.InvariantCulture, $";ReconnectBaseDelay={ReconnectBaseDelay};ReconnectMaxDelay={ReconnectMaxDelay};BacklogPolicy={BacklogPolicy}");
        builder.Append(CultureInfo.InvariantCulture, $";MultiplexedConnectionCount={MultiplexedConnectionCount};DedicatedPoolSize={DedicatedPoolSize}");
        builder.Append(CultureInfo.InvariantCulture, $";MaxBulkLength={MaxBulkLength};MaxNestingDepth={MaxNestingDepth}");
        builder.Append(CultureInfo.InvariantCulture, $";SubscriptionBufferSize={SubscriptionBufferSize};SubscriptionOverflowPolicy={SubscriptionOverflowPolicy}");
        builder.Append(CultureInfo.InvariantCulture, $";RecordCommandArguments={RecordCommandArguments}");
        return builder.ToString();
    }

    /// <summary>Copies the options so that the client is not affected by later changes.</summary>
    internal RedisClientOptions Clone()
    {
        var copy = new RedisClientOptions
        {
            Username = Username,
            Password = Password,
            CredentialsProvider = CredentialsProvider,
            Database = Database,
            ClientName = ClientName,
            Protocol = Protocol,
            Ssl = Ssl,
            SslOptions = SslOptions,
            ConnectTimeout = ConnectTimeout,
            CommandTimeout = CommandTimeout,
            KeepAliveInterval = KeepAliveInterval,
            ReconnectBaseDelay = ReconnectBaseDelay,
            ReconnectMaxDelay = ReconnectMaxDelay,
            BacklogPolicy = BacklogPolicy,
            MultiplexedConnectionCount = MultiplexedConnectionCount,
            DedicatedPoolSize = DedicatedPoolSize,
            MaxBulkLength = MaxBulkLength,
            MaxNestingDepth = MaxNestingDepth,
            SubscriptionBufferSize = SubscriptionBufferSize,
            SubscriptionOverflowPolicy = SubscriptionOverflowPolicy,
            RecordCommandArguments = RecordCommandArguments,
        };
        foreach (var endpoint in Endpoints)
        {
            copy.Endpoints.Add(endpoint);
        }

        return copy;
    }

    internal static string FormatEndpoint(EndPoint endpoint) => endpoint switch
    {
        DnsEndPoint dns => $"{dns.Host}:{dns.Port}",
        IPEndPoint ip => ip.ToString(),
        _ => endpoint.ToString() ?? string.Empty,
    };
}
