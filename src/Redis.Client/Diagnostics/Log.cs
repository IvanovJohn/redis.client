using Microsoft.Extensions.Logging;

namespace Redis.Client.Diagnostics;

/// <summary>
/// All log events of the client. Messages carry endpoints, connection ids and command names only: never keys,
/// values, arguments or credentials.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(1, LogLevel.Information, "Redis {Kind} connection #{ConnectionId} established to {Endpoint} (RESP{Protocol}, server {ServerVersion})")]
    public static partial void Connected(ILogger logger, string kind, long connectionId, string endpoint, int protocol, string serverVersion);

    [LoggerMessage(2, LogLevel.Warning, "Redis {Kind} connection #{ConnectionId} to {Endpoint} lost")]
    public static partial void ConnectionLost(ILogger logger, string kind, long connectionId, string endpoint, Exception? exception);

    [LoggerMessage(3, LogLevel.Warning, "Connecting to Redis {Endpoint} failed (attempt {Attempt}); next attempt in {Delay}")]
    public static partial void ConnectFailed(ILogger logger, string endpoint, int attempt, TimeSpan delay, Exception exception);

    [LoggerMessage(4, LogLevel.Information, "Redis {Kind} connection to {Endpoint} restored after {Attempts} attempt(s)")]
    public static partial void Reconnected(ILogger logger, string kind, string endpoint, int attempts);

    [LoggerMessage(5, LogLevel.Information, "Redis {Kind} connection #{ConnectionId} to {Endpoint} closed")]
    public static partial void Closed(ILogger logger, string kind, long connectionId, string endpoint);

    [LoggerMessage(6, LogLevel.Information, "Server {Endpoint} rejected HELLO; falling back to RESP2")]
    public static partial void FallbackToResp2(ILogger logger, string endpoint);

    [LoggerMessage(7, LogLevel.Warning, "CLIENT SETNAME failed on {Endpoint}; continuing without a connection name: {Error}")]
    public static partial void SetNameFailed(ILogger logger, string endpoint, string error);

    [LoggerMessage(8, LogLevel.Error, "Protocol error on Redis connection #{ConnectionId} to {Endpoint}; the connection is closed")]
    public static partial void ProtocolError(ILogger logger, long connectionId, string endpoint, Exception exception);

    [LoggerMessage(9, LogLevel.Warning, "Dedicated connection pool for {Endpoint} is exhausted ({PoolSize} connections in use); waiting for a free connection")]
    public static partial void DedicatedPoolExhausted(ILogger logger, string endpoint, int poolSize);

    [LoggerMessage(10, LogLevel.Error, "Subscription handler for {Channel} threw an exception")]
    public static partial void SubscriptionHandlerFailed(ILogger logger, string channel, Exception exception);

    [LoggerMessage(11, LogLevel.Warning, "Keep-alive PING on Redis connection #{ConnectionId} to {Endpoint} timed out; reconnecting")]
    public static partial void KeepAliveTimedOut(ILogger logger, long connectionId, string endpoint);

    [LoggerMessage(12, LogLevel.Debug, "Command {Command} on connection #{ConnectionId} timed out after it was sent; its reply will be discarded")]
    public static partial void CommandAbandoned(ILogger logger, string command, long connectionId);

    [LoggerMessage(13, LogLevel.Information, "Restored {Count} subscription(s) on {Endpoint}")]
    public static partial void SubscriptionsRestored(ILogger logger, int count, string endpoint);
}
