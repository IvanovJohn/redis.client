using Microsoft.Extensions.Logging.Abstractions;
using Redis.Client.Connections;
using Redis.Client.Protocol;
using Redis.Client.Tests.Fakes;
using static Redis.Client.Tests.Connections.MultiplexedConnectionTests;

namespace Redis.Client.Tests.Connections;

public sealed class PhysicalConnectionTests
{
    [Fact]
    public async Task Dispose_never_throws_even_with_unsent_data_on_a_dead_connection()
    {
        await using var server = new FakeRedisServer();
        var connection = await PhysicalConnection.ConnectAsync(server.CreateOptions(), ConnectionKind.Multiplexed, NullLogger.Instance, default);

        // Buffer data without flushing, then lose the connection: disposing must not try (and fail) to send it.
        for (var i = 0; i < 1000; i++)
        {
            connection.Write(new RedisCommand(Commands.Set, "k", new string('x', 100)));
        }

        server.DropConnections();
        await WaitUntilAsync(() => server.ConnectionCount == 0);
        await Task.Delay(50);

        await connection.DisposeAsync();
        await connection.DisposeAsync();
        Assert.True(connection.IsDisposed);
    }
}
