using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Redis.Client.IntegrationTests.Infrastructure;

public sealed record RedisServerOptions
{
    /// <summary>Expose a TLS port in addition to the plain one.</summary>
    public bool Tls { get; init; }

    /// <summary>Value of <c>tls-auth-clients</c>: <c>no</c>, <c>optional</c> or <c>yes</c>.</summary>
    public string TlsAuthClients { get; init; } = "optional";

    /// <summary>Require a password for the default user.</summary>
    public string? DefaultUserPassword { get; init; }

    /// <summary>Disable HELLO (<c>rename-command HELLO ""</c>) to behave like a proxy without RESP3 support.</summary>
    public bool DisableHello { get; init; }
}

/// <summary>
/// A Redis server in a Docker container. Host ports are fixed for the lifetime of the object,
/// so tests can restart the container and the client reconnects to the same endpoint.
/// </summary>
public sealed class RedisServer : IAsyncDisposable
{
    public const string AclUser = "app";
    public const string AclPassword = "app-secret";

    private readonly IContainer _container;

    private RedisServer(IContainer container, int port, int? tlsPort, TestCertificates? certificates)
    {
        _container = container;
        Port = port;
        TlsPort = tlsPort;
        Certificates = certificates;
    }

    /// <summary>Image under test; CI runs the suite once per supported Redis version.</summary>
    public static string Image => Environment.GetEnvironmentVariable("REDIS_IMAGE") ?? "redis:7.4-alpine";

    /// <summary>Major.minor version parsed from <see cref="Image"/> (e.g. 7.4 or 6.2).</summary>
    public static Version ImageVersion
    {
        get
        {
            var tag = Image[(Image.LastIndexOf(':') + 1)..];
            var numeric = new string(tag.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            return Version.TryParse(numeric.Contains('.') ? numeric : numeric + ".0", out var v) ? v : new Version(7, 4);
        }
    }

    public string Host => "127.0.0.1";

    public int Port { get; }

    public int? TlsPort { get; }

    public TestCertificates? Certificates { get; }

    public string Endpoint => $"{Host}:{Port}";

    /// <summary>Options for the ACL user over the plain port.</summary>
    public RedisClientOptions CreateOptions(Action<RedisClientOptions>? configure = null)
    {
        var options = new RedisClientOptions
        {
            Username = AclUser,
            Password = AclPassword,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        options.Endpoints.Add(new IPEndPoint(IPAddress.Loopback, Port));
        configure?.Invoke(options);
        return options;
    }

    /// <summary>Options for the ACL user over the TLS port, trusting the test CA.</summary>
    public RedisClientOptions CreateTlsOptions(Action<RedisClientOptions>? configure = null)
    {
        var certificates = Certificates ?? throw new InvalidOperationException("Server was started without TLS.");
        var options = CreateOptions();
        options.Endpoints.Clear();
        options.Endpoints.Add(new DnsEndPoint("localhost", TlsPort!.Value));
        options.Ssl = true;
        options.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            CertificateChainPolicy = new System.Security.Cryptography.X509Certificates.X509ChainPolicy
            {
                TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust,
                CustomTrustStore = { certificates.Ca },
                RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
            },
        };
        configure?.Invoke(options);
        return options;
    }

    public static async Task<RedisServer> StartAsync(RedisServerOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new RedisServerOptions();
        var port = GetFreePort();
        int? tlsPort = options.Tls ? GetFreePort() : null;

        var command = new List<string>
        {
            "redis-server",
            "--save", "",
            "--appendonly", "no",
            // redis-server joins the words after --user into one ACL rule, so each word is a separate argument.
            "--user", AclUser, "on", $">{AclPassword}", "~*", "&*", "+@all",
        };
        command.AddRange(options.DefaultUserPassword is { } password
            ? ["--user", "default", "on", $">{password}", "~*", "&*", "+@all"]
            : ["--user", "default", "on", "nopass", "~*", "&*", "+@all"]);
        if (options.DisableHello)
        {
            command.AddRange(["--rename-command", "HELLO", ""]);
        }

        if (ImageVersion >= new Version(7, 0))
        {
            command.AddRange(["--enable-debug-command", "yes"]);
        }

        var builder = new ContainerBuilder(Image)
            .WithPortBinding(port, 6379);

        TestCertificates? certificates = null;
        if (options.Tls)
        {
            certificates = TestCertificates.Create();
            builder = builder
                .WithPortBinding(tlsPort!.Value, 6380)
                .WithResourceMapping(TestCertificates.CertificatePem(certificates.Ca), "/tls/ca.crt")
                .WithResourceMapping(TestCertificates.CertificatePem(certificates.Server), "/tls/server.crt")
                .WithResourceMapping(TestCertificates.PrivateKeyPem(certificates.Server), "/tls/server.key");
            command.AddRange(
            [
                "--tls-port", "6380",
                "--tls-cert-file", "/tls/server.crt",
                "--tls-key-file", "/tls/server.key",
                "--tls-ca-cert-file", "/tls/ca.crt",
                "--tls-auth-clients", options.TlsAuthClients,
            ]);
        }

        var cliAuth = options.DefaultUserPassword is null ? "" : $" --user {AclUser} --pass {AclPassword} --no-auth-warning";
        var container = builder
            .WithCommand([.. command])
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("sh", "-c", $"redis-cli{cliAuth} ping | grep -q PONG"))
            .Build();

        await container.StartAsync(cancellationToken);
        return new RedisServer(container, port, tlsPort, certificates);
    }

    /// <summary>Runs <c>redis-cli</c> inside the container as the ACL user and returns its stdout.</summary>
    public async Task<string> CliAsync(params string[] args)
    {
        var result = await _container.ExecAsync(["redis-cli", "--user", AclUser, "--pass", AclPassword, "--no-auth-warning", .. args]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"redis-cli failed ({result.ExitCode}): {result.Stderr}");
        }

        return result.Stdout.TrimEnd('\n');
    }

    public async Task<string> CliTlsAsync(params string[] args)
    {
        var result = await _container.ExecAsync(
        [
            "redis-cli", "--tls", "-p", "6380", "--cacert", "/tls/ca.crt",
            "--user", AclUser, "--pass", AclPassword, "--no-auth-warning", .. args,
        ]);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"redis-cli --tls failed ({result.ExitCode}): {result.Stderr}");
        }

        return result.Stdout.TrimEnd('\n');
    }

    public async Task RestartAsync(TimeSpan downtime = default)
    {
        await _container.StopAsync();
        if (downtime > TimeSpan.Zero)
        {
            await Task.Delay(downtime);
        }

        await _container.StartAsync();
    }

    public Task PauseAsync() => _container.PauseAsync();

    public Task UnpauseAsync() => _container.UnpauseAsync();

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        Certificates?.Dispose();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
