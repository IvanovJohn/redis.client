using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Redis.Client.Connections;
using Redis.Client.IntegrationTests.Infrastructure;
using Redis.Client.Protocol;

namespace Redis.Client.IntegrationTests.Connections;

[Collection(RedisCollection.Name)]
public sealed class HandshakeTests(RedisFixture fixture)
{
    private RedisServer Server => fixture.Server;

    [Fact]
    public async Task Acl_user_connects_over_resp3_by_default()
    {
        await using var connection = await ConnectAsync(Server.CreateOptions(o => o.ClientName = "handshake-test"));
        Assert.Equal(3, connection.Protocol);
        Assert.Equal(RedisServer.ImageVersion.Major, connection.ServerVersion!.Major);
        Assert.Equal(RedisServer.AclUser, await connection.ExecuteAsync(new RedisCommand(Commands.Lookup("ACL"), "WHOAMI"), f => f.GetString(), default));
        Assert.Equal("handshake-test", await connection.ExecuteAsync(new RedisCommand(Commands.Client, "GETNAME"), f => f.GetString(), default));
    }

    [Fact]
    public async Task Wrong_password_fails_without_leaking_it()
    {
        var options = Server.CreateOptions(o => o.Password = "TopSecretWrong");
        var error = await Assert.ThrowsAsync<RedisAuthenticationException>(() => ConnectAsync(options));
        Assert.DoesNotContain("TopSecretWrong", error.ToString(), StringComparison.Ordinal);
        Assert.False(error.MayHaveExecuted);
    }

    [Fact]
    public async Task Selected_database_is_applied()
    {
        await using var connection = await ConnectAsync(Server.CreateOptions(o => o.Database = 3));
        var info = await connection.ExecuteAsync(new RedisCommand(Commands.Client, "INFO"), f => f.GetString(), default);
        Assert.Contains(" db=3 ", info, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resp2_can_be_forced()
    {
        await using var connection = await ConnectAsync(Server.CreateOptions(o =>
        {
            o.Protocol = RedisProtocol.Resp2;
            o.ClientName = "resp2-test";
        }));
        Assert.Equal(2, connection.Protocol);
        Assert.Equal(RedisServer.ImageVersion.Major, connection.ServerVersion!.Major);
        Assert.Equal("resp2-test", await connection.ExecuteAsync(new RedisCommand(Commands.Client, "GETNAME"), f => f.GetString(), default));
    }

    [Fact]
    public async Task Credentials_provider_is_called_on_connect()
    {
        var calls = 0;
        var options = Server.CreateOptions(o =>
        {
            o.Password = null;
            o.Username = null;
            o.CredentialsProvider = _ =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(new RedisCredentials(RedisServer.AclUser, RedisServer.AclPassword));
            };
        });
        await using (await ConnectAsync(options))
        {
        }

        await using (await ConnectAsync(options))
        {
        }

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Tls_with_trusted_ca_connects()
    {
        await using var connection = await ConnectAsync(Server.CreateTlsOptions());
        Assert.Equal("PONG", await connection.ExecuteAsync(new RedisCommand(Commands.Ping), f => f.GetString(), default));
    }

    [Fact]
    public async Task Tls_with_untrusted_certificate_fails()
    {
        var options = Server.CreateTlsOptions(o => o.SslOptions = null);
        var error = await Assert.ThrowsAsync<RedisConnectionException>(() => ConnectAsync(options));
        Assert.IsType<AuthenticationException>(error.InnerException);
    }

    [Fact]
    public async Task Connect_timeout_is_enforced()
    {
        var options = Server.CreateOptions(o =>
        {
            o.Endpoints.Clear();
            o.Endpoints.Add(new IPEndPoint(IPAddress.Parse("10.255.255.1"), 6379));
            o.ConnectTimeout = TimeSpan.FromMilliseconds(300);
        });
        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<RedisConnectionException>(() => ConnectAsync(options));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"took {stopwatch.Elapsed}");
    }

    internal static Task<PhysicalConnection> ConnectAsync(RedisClientOptions options) =>
        PhysicalConnection.ConnectAsync(options, ConnectionKind.Dedicated, NullLogger.Instance, CancellationToken.None);
}

/// <summary>Handshake scenarios that need a dedicated server configuration.</summary>
public sealed class SpecialServerHandshakeTests
{
    [Fact]
    public async Task Auto_protocol_falls_back_to_resp2_when_hello_is_rejected()
    {
        await using var server = await RedisServer.StartAsync(new RedisServerOptions { DisableHello = true });
        await using var connection = await HandshakeTests.ConnectAsync(server.CreateOptions(o => o.ClientName = "fallback"));
        Assert.Equal(2, connection.Protocol);
        Assert.Equal("fallback", await connection.ExecuteAsync(new RedisCommand(Commands.Client, "GETNAME"), f => f.GetString(), default));

        var error = await Assert.ThrowsAsync<RedisConnectionException>(() => HandshakeTests.ConnectAsync(server.CreateOptions(o => o.Protocol = RedisProtocol.Resp3)));
        Assert.Contains("RESP3 is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mutual_tls_requires_client_certificate()
    {
        await using var server = await RedisServer.StartAsync(new RedisServerOptions { Tls = true, TlsAuthClients = "yes" });
        await using (var connection = await HandshakeTests.ConnectAsync(server.CreateTlsOptions(o =>
                     {
                         o.SslOptions!.ClientCertificates = [server.Certificates!.Client];
                     })))
        {
            Assert.Equal("PONG", await connection.ExecuteAsync(new RedisCommand(Commands.Ping), f => f.GetString(), default));
        }

        await Assert.ThrowsAsync<RedisConnectionException>(() => HandshakeTests.ConnectAsync(server.CreateTlsOptions()));
    }

    [Fact]
    public async Task Missing_credentials_for_protected_default_user_is_authentication_error()
    {
        await using var server = await RedisServer.StartAsync(new RedisServerOptions { DefaultUserPassword = "defaultpw" });
        var options = server.CreateOptions(o =>
        {
            o.Username = null;
            o.Password = null;
        });
        // HELLO itself is rejected with NOAUTH, so the connect fails up front instead of every later command.
        var error = await Assert.ThrowsAsync<RedisAuthenticationException>(() => HandshakeTests.ConnectAsync(options));
        Assert.Contains("NOAUTH", error.Message, StringComparison.Ordinal);

        await using var authenticated = await HandshakeTests.ConnectAsync(server.CreateOptions(o =>
        {
            o.Username = null;
            o.Password = "defaultpw";
        }));
        Assert.Equal("PONG", await authenticated.ExecuteAsync(new RedisCommand(Commands.Ping), f => f.GetString(), default));
    }
}
