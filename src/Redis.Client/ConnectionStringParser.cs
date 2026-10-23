using System.Globalization;
using System.Net;

namespace Redis.Client;

/// <summary>Parses <c>redis://</c> and <c>rediss://</c> connection strings. Errors never echo the input.</summary>
internal static class ConnectionStringParser
{
    public static void Apply(string connectionString, RedisClientOptions options)
    {
        if (!Uri.TryCreate(connectionString.Trim(), UriKind.Absolute, out var uri))
        {
            throw new FormatException("The Redis connection string is not a valid URI. Expected redis://[[username]:password@]host[:port][/database][?options].");
        }

        options.Ssl = uri.Scheme.ToLowerInvariant() switch
        {
            "redis" => false,
            "rediss" => true,
            _ => throw new FormatException($"Unsupported connection string scheme '{uri.Scheme}'. Use redis:// or rediss://."),
        };

        if (string.IsNullOrEmpty(uri.Host))
        {
            throw new FormatException("The Redis connection string has no host.");
        }

        var port = uri.IsDefaultPort || uri.Port < 0 ? RedisClientOptions.DefaultPort : uri.Port;
        var host = uri.HostNameType == UriHostNameType.IPv6 ? uri.Host.Trim('[', ']') : uri.Host;
        options.Endpoints.Clear();
        options.Endpoints.Add(IPAddress.TryParse(host, out var address) ? new IPEndPoint(address, port) : new DnsEndPoint(host, port));

        ApplyUserInfo(uri.UserInfo, options);
        ApplyDatabase(uri.AbsolutePath, options);
        foreach (var (name, value) in ParseQuery(uri.Query))
        {
            ApplyOption(name, value, options);
        }
    }

    private static void ApplyUserInfo(string userInfo, RedisClientOptions options)
    {
        if (string.IsNullOrEmpty(userInfo))
        {
            return;
        }

        var colon = userInfo.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            // redis://password@host is a common shorthand for the default user's password.
            options.Password = Uri.UnescapeDataString(userInfo);
            return;
        }

        var user = Uri.UnescapeDataString(userInfo[..colon]);
        options.Username = user.Length == 0 ? null : user;
        options.Password = Uri.UnescapeDataString(userInfo[(colon + 1)..]);
    }

    private static void ApplyDatabase(string path, RedisClientOptions options)
    {
        var trimmed = path.Trim('/');
        if (trimmed.Length == 0)
        {
            return;
        }

        options.Database = int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var database)
            ? database
            : throw new FormatException($"The database in the connection string path must be a non-negative number, got '{trimmed}'.");
    }

    private static IEnumerable<(string Name, string Value)> ParseQuery(string query)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var name = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..]);
            yield return (name, value);
        }
    }

    internal static void ApplyOption(string name, string value, RedisClientOptions options)
    {
        switch (name.ToLowerInvariant())
        {
            case "clientname":
                options.ClientName = value;
                break;
            case "database":
                options.Database = ParseInt(name, value);
                break;
            case "protocol":
                options.Protocol = value.ToLowerInvariant() switch
                {
                    "auto" => RedisProtocol.Auto,
                    "2" or "resp2" => RedisProtocol.Resp2,
                    "3" or "resp3" => RedisProtocol.Resp3,
                    _ => throw Invalid(name, "auto, resp2 or resp3"),
                };
                break;
            case "connecttimeout":
                options.ConnectTimeout = ParseTime(name, value);
                break;
            case "commandtimeout":
                options.CommandTimeout = ParseTime(name, value);
                break;
            case "keepaliveinterval":
                options.KeepAliveInterval = ParseTime(name, value);
                break;
            case "reconnectbasedelay":
                options.ReconnectBaseDelay = ParseTime(name, value);
                break;
            case "reconnectmaxdelay":
                options.ReconnectMaxDelay = ParseTime(name, value);
                break;
            case "backlogpolicy":
                options.BacklogPolicy = ParseEnum<BacklogPolicy>(name, value);
                break;
            case "multiplexedconnectioncount":
                options.MultiplexedConnectionCount = ParseInt(name, value);
                break;
            case "dedicatedpoolsize":
                options.DedicatedPoolSize = ParseInt(name, value);
                break;
            case "maxbulklength":
                options.MaxBulkLength = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) ? length : throw Invalid(name, "an integer");
                break;
            case "maxnestingdepth":
                options.MaxNestingDepth = ParseInt(name, value);
                break;
            case "subscriptionbuffersize":
                options.SubscriptionBufferSize = ParseInt(name, value);
                break;
            case "subscriptionoverflowpolicy":
                options.SubscriptionOverflowPolicy = ParseEnum<SubscriptionOverflowPolicy>(name, value);
                break;
            case "recordcommandarguments":
                options.RecordCommandArguments = bool.TryParse(value, out var record) ? record : throw Invalid(name, "true or false");
                break;
            default:
                throw new FormatException($"Unknown connection string option '{name}'.");
        }
    }

    private static int ParseInt(string name, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : throw Invalid(name, "an integer");

    private static TimeSpan ParseTime(string name, string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds))
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time)
            ? time
            : throw Invalid(name, "a time span (00:00:05) or a number of milliseconds");
    }

    private static TEnum ParseEnum<TEnum>(string name, string value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var result) && Enum.IsDefined(result) && !int.TryParse(value, out _)
            ? result
            : throw Invalid(name, string.Join(", ", Enum.GetNames<TEnum>()));

    private static FormatException Invalid(string name, string expected) =>
        new($"Invalid value of connection string option '{name}': expected {expected}.");
}
