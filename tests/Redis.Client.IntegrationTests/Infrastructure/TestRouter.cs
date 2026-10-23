using Microsoft.Extensions.Logging.Abstractions;
using Redis.Client.Connections;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;

namespace Redis.Client.IntegrationTests.Infrastructure;

/// <summary>Builds the internal connection stack directly, below the public client API.</summary>
internal static class TestRouter
{
    public static SingleNodeRouter Create(RedisClientOptions options)
    {
        var instrumentation = new ClientInstrumentation(options, null, () => 0, () => 0);
        return new SingleNodeRouter(options, NullLogger.Instance, instrumentation);
    }

    public static Task<long> IntegerAsync(this ICommandRouter router, string command, params RedisValue[] args) =>
        router.ExecuteAsync(new RedisCommand(Commands.Lookup(command), args), f => f.GetInt64(), default);

    public static Task<RedisValue> ValueAsync(this ICommandRouter router, string command, params RedisValue[] args) =>
        router.ExecuteAsync(new RedisCommand(Commands.Lookup(command), args), f => f.ToRedisValue(), default);

    public static Task<string> StringAsync(this ICommandRouter router, string command, params RedisValue[] args) =>
        router.ExecuteAsync(new RedisCommand(Commands.Lookup(command), args), f => f.GetString(), default);

    /// <summary>Number of server connections whose CLIENT LIST name equals <paramref name="clientName"/>.</summary>
    public static async Task<int> CountConnectionsAsync(RedisServer server, string clientName)
    {
        var list = await server.CliAsync("CLIENT", "LIST");
        return list.Split('\n').Count(line => line.Contains($" name={clientName} ", StringComparison.Ordinal));
    }
}
