using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;

namespace Redis.Client.Connections;

/// <summary>Role of a connection; used in logs and metrics.</summary>
internal enum ConnectionKind
{
    Multiplexed,
    Dedicated,
    Subscriber,
}

/// <summary>
/// One TCP (optionally TLS) connection that has completed the handshake. It knows how to write commands and read
/// frames; higher layers decide how commands and replies are matched.
/// </summary>
internal sealed class PhysicalConnection : IAsyncDisposable
{
    private static long s_nextId;

    private readonly Socket _socket;
    private readonly Stream _stream;
    private readonly ILogger _logger;
    private int _disposed;

    private PhysicalConnection(Socket socket, Stream stream, EndPoint endpoint, ConnectionKind kind, RespLimits limits, ILogger logger)
    {
        _socket = socket;
        _stream = stream;
        _logger = logger;
        Endpoint = endpoint;
        EndpointText = RedisClientOptions.FormatEndpoint(endpoint);
        Kind = kind;
        Id = Interlocked.Increment(ref s_nextId);
        Scanner = new RespScanner(limits);
        Reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 16 * 1024, minimumReadSize: 4 * 1024, leaveOpen: true));
        Writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(minimumBufferSize: 16 * 1024, leaveOpen: true));
    }

    public long Id { get; }

    public EndPoint Endpoint { get; }

    public string EndpointText { get; }

    public ConnectionKind Kind { get; }

    /// <summary>Negotiated RESP version: 2 or 3.</summary>
    public int Protocol { get; private set; } = 2;

    public Version? ServerVersion { get; private set; }

    public PipeReader Reader { get; }

    public PipeWriter Writer { get; }

    public RespScanner Scanner { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Connects, negotiates TLS and performs the handshake within <see cref="RedisClientOptions.ConnectTimeout"/>.</summary>
    public static async Task<PhysicalConnection> ConnectAsync(RedisClientOptions options, ConnectionKind kind, ILogger logger, CancellationToken cancellationToken)
    {
        var endpoint = options.Endpoints[0];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectTimeout);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        PhysicalConnection? connection = null;
        try
        {
            socket.NoDelay = true;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            await socket.ConnectAsync(endpoint, timeout.Token).ConfigureAwait(false);

            Stream stream = new NetworkStream(socket, ownsSocket: false);
            if (options.Ssl)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                stream = ssl;
                await ssl.AuthenticateAsClientAsync(CreateSslOptions(options, endpoint), timeout.Token).ConfigureAwait(false);
            }

            var limits = new RespLimits(options.MaxBulkLength, options.MaxNestingDepth);
            connection = new PhysicalConnection(socket, stream, endpoint, kind, limits, logger);
            await connection.HandshakeAsync(options, timeout.Token).ConfigureAwait(false);
            Log.Connected(logger, kind.ToString(), connection.Id, connection.EndpointText, connection.Protocol, connection.ServerVersion?.ToString() ?? "unknown");
            return connection;
        }
        catch (Exception ex)
        {
            if (connection is not null)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                socket.Dispose();
            }

            if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new RedisConnectionException($"Connecting to {RedisClientOptions.FormatEndpoint(endpoint)} timed out after {options.ConnectTimeout}.", ex);
            }

            if (ex is RedisConnectionException or OperationCanceledException)
            {
                throw;
            }

            throw new RedisConnectionException($"Connecting to {RedisClientOptions.FormatEndpoint(endpoint)} failed: {ex.Message}", ex);
        }
    }

    /// <summary>Serializes a command into the write buffer; call <see cref="FlushAsync"/> to send it.</summary>
    public void Write(RedisCommand command) => RespWriter.WriteCommand(Writer, command);

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        var result = await Writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsCompleted)
        {
            throw new RedisConnectionException($"Connection to {EndpointText} is closed.");
        }
    }

    /// <summary>
    /// Reads the next reply and converts it while its buffer is valid. RESP3 push frames are skipped.
    /// Used by request/response style callers (handshake, dedicated connections).
    /// </summary>
    public async ValueTask<T> ReadReplyAsync<T>(Func<RespFrame, T> convert, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;
            if (Scanner.TryScan(buffer, out var length))
            {
                var frameBuffer = buffer.Slice(0, length);
                var frame = RespFrame.Read(frameBuffer);
                try
                {
                    if (frame.Type != RespType.Push)
                    {
                        return convert(frame);
                    }
                }
                finally
                {
                    Reader.AdvanceTo(frameBuffer.End);
                }

                continue;
            }

            Reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                throw new RedisConnectionException($"Connection to {EndpointText} was closed by the server.");
            }
        }
    }

    /// <summary>Sends one command and reads its reply (no concurrency: request/response connections only).</summary>
    public async ValueTask<T> ExecuteAsync<T>(RedisCommand command, Func<RespFrame, T> convert, CancellationToken cancellationToken)
    {
        Write(command);
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await ReadReplyAsync(convert, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Checks whether an idle connection has been closed by the peer without blocking.</summary>
    public bool IsAlive()
    {
        if (IsDisposed)
        {
            return false;
        }

        try
        {
            return !(_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0);
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Aborts the connection immediately; pending reads and writes fail.</summary>
    public void Abort()
    {
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Disposal must never throw: callers run it on failure paths (and a throwing dispose once silently ended
        // the reconnect loop). Completing the writer with an exception drops unsent data instead of flushing it
        // into a dead socket.
        Abort();
        var closed = new ObjectDisposedException(nameof(PhysicalConnection));
        try
        {
            await Reader.CompleteAsync(closed).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            await Writer.CompleteAsync(closed).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        _socket.Dispose();
        Log.Closed(_logger, Kind.ToString(), Id, EndpointText);
    }

    public override string ToString() => $"#{Id} {Kind} {EndpointText}";

    private static SslClientAuthenticationOptions CreateSslOptions(RedisClientOptions options, EndPoint endpoint)
    {
        var source = options.SslOptions;
        var host = endpoint switch
        {
            DnsEndPoint dns => dns.Host,
            IPEndPoint ip => ip.Address.ToString(),
            _ => string.Empty,
        };
        if (source is null)
        {
            return new SslClientAuthenticationOptions { TargetHost = host };
        }

        // Copy so that the caller's object is never modified.
        return new SslClientAuthenticationOptions
        {
            TargetHost = source.TargetHost ?? host,
            ClientCertificates = source.ClientCertificates,
            ClientCertificateContext = source.ClientCertificateContext,
            LocalCertificateSelectionCallback = source.LocalCertificateSelectionCallback,
            RemoteCertificateValidationCallback = source.RemoteCertificateValidationCallback,
            CertificateChainPolicy = source.CertificateChainPolicy,
            CertificateRevocationCheckMode = source.CertificateRevocationCheckMode,
            EnabledSslProtocols = source.EnabledSslProtocols,
            EncryptionPolicy = source.EncryptionPolicy,
            CipherSuitesPolicy = source.CipherSuitesPolicy,
            ApplicationProtocols = source.ApplicationProtocols,
            AllowRenegotiation = source.AllowRenegotiation,
            AllowTlsResume = source.AllowTlsResume,
        };
    }

    private async Task HandshakeAsync(RedisClientOptions options, CancellationToken cancellationToken)
    {
        var credentials = options.CredentialsProvider is not null
            ? await options.CredentialsProvider(cancellationToken).ConfigureAwait(false)
            : options.Password is not null ? new RedisCredentials(options.Username, options.Password) : null;

        var resp3 = false;
        if (options.Protocol != RedisProtocol.Resp2)
        {
            resp3 = await TryHelloAsync(options, credentials, cancellationToken).ConfigureAwait(false);
        }

        if (!resp3)
        {
            await Resp2HandshakeAsync(options, credentials, cancellationToken).ConfigureAwait(false);
        }

        if (options.Database != 0)
        {
            var error = await ExecuteAsync(new RedisCommand(Commands.Select, options.Database), ErrorOrNull, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                throw new RedisConnectionException($"SELECT {options.Database} failed: {error.Message}", error);
            }
        }
    }

    /// <summary>Sends <c>HELLO 3</c>; returns <see langword="false"/> if RESP2 must be used instead.</summary>
    private async Task<bool> TryHelloAsync(RedisClientOptions options, RedisCredentials? credentials, CancellationToken cancellationToken)
    {
        var args = new List<RedisValue> { 3 };
        if (credentials is not null)
        {
            args.Add("AUTH");
            args.Add(credentials.Username ?? "default");
            args.Add(credentials.Password);
        }

        if (options.ClientName is not null)
        {
            args.Add("SETNAME");
            args.Add(options.ClientName);
        }

        var (error, version) = await ExecuteAsync(new RedisCommand(Commands.Hello, [.. args]), ReadHello, cancellationToken).ConfigureAwait(false);
        if (error is null)
        {
            Protocol = 3;
            ServerVersion = version;
            return true;
        }

        if (IsAuthenticationError(error))
        {
            throw AuthenticationFailed(error);
        }

        if (options.Protocol == RedisProtocol.Resp3)
        {
            throw new RedisConnectionException($"The server rejected HELLO 3 and RESP3 is required: {error.Message}", error);
        }

        Log.FallbackToResp2(_logger, EndpointText);
        return false;
    }

    private async Task Resp2HandshakeAsync(RedisClientOptions options, RedisCredentials? credentials, CancellationToken cancellationToken)
    {
        Protocol = 2;
        if (credentials is not null)
        {
            var auth = credentials.Username is null
                ? new RedisCommand(Commands.Auth, credentials.Password)
                : new RedisCommand(Commands.Auth, credentials.Username, credentials.Password);
            var error = await ExecuteAsync(auth, ErrorOrNull, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                throw AuthenticationFailed(error);
            }
        }

        if (options.ClientName is not null)
        {
            var error = await ExecuteAsync(new RedisCommand(Commands.Client, "SETNAME", options.ClientName), ErrorOrNull, cancellationToken).ConfigureAwait(false);
            if (error is not null)
            {
                Log.SetNameFailed(_logger, EndpointText, error.Message);
            }
        }

        ServerVersion = await ExecuteAsync(new RedisCommand(Commands.Info, "server"), ReadInfoVersion, cancellationToken).ConfigureAwait(false);
    }

    private static RedisServerException? ErrorOrNull(RespFrame frame) => frame.IsError ? frame.ToServerException() : null;

    private static (RedisServerException? Error, Version? Version) ReadHello(RespFrame frame)
    {
        if (frame.IsError)
        {
            return (frame.ToServerException(), null);
        }

        Version? version = null;
        if (frame.Type == RespType.Map)
        {
            var isKey = true;
            var versionNext = false;
            foreach (var child in frame.GetChildren())
            {
                if (isKey)
                {
                    versionNext = child.GetString() == "version";
                }
                else if (versionNext)
                {
                    version = ParseVersion(child.GetString());
                }

                isKey = !isKey;
            }
        }

        return (null, version);
    }

    private static Version? ReadInfoVersion(RespFrame frame)
    {
        if (frame.IsError || frame.IsNull || frame.IsAggregate)
        {
            return null;
        }

        foreach (var line in frame.GetString().Split('\n'))
        {
            if (line.StartsWith("redis_version:", StringComparison.Ordinal))
            {
                return ParseVersion(line["redis_version:".Length..].Trim());
            }
        }

        return null;
    }

    private static Version? ParseVersion(string text) => Version.TryParse(text, out var version) ? version : null;

    private static bool IsAuthenticationError(RedisServerException error) =>
        error.ErrorCode is "WRONGPASS" or "NOAUTH" or "NOPERM"
        || error.Message.Contains("invalid password", StringComparison.OrdinalIgnoreCase)
        || error.Message.Contains("invalid username-password", StringComparison.OrdinalIgnoreCase);

    private RedisAuthenticationException AuthenticationFailed(RedisServerException error) =>
        new($"Authentication to {EndpointText} failed: {error.Message}", error);
}
