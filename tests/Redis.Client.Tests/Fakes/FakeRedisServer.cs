using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Redis.Client.Protocol;

namespace Redis.Client.Tests.Fakes;

/// <summary>A command received by <see cref="FakeRedisServer"/>.</summary>
public sealed record FakeCommand(int ConnectionId, string[] Arguments)
{
    public string Name => Arguments[0].ToUpperInvariant();

    /// <summary>The first argument (usually the key), or an empty string.</summary>
    public string Key => Arguments.Length > 1 ? Arguments[1] : string.Empty;

    public override string ToString() => string.Join(' ', Arguments);
}

/// <summary>
/// A minimal in-process RESP server for deterministic tests of failure modes a real Redis will not produce
/// (garbage replies, stalls, dropped connections). The handshake (HELLO, AUTH, CLIENT, SELECT, INFO) is answered
/// automatically; every other command goes to <see cref="Handler"/>, which returns raw reply bytes (or
/// <see langword="null"/> to send nothing).
/// </summary>
public sealed class FakeRedisServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, Socket> _clients = new();
    private readonly Task _acceptLoop;
    private int _nextId;

    public FakeRedisServer()
    {
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public ConcurrentQueue<FakeCommand> Received { get; } = new();

    /// <summary>Replies to non-handshake commands. Default: <c>+OK</c>.</summary>
    public Func<FakeCommand, string?> Handler { get; set; } = _ => "+OK\r\n";

    /// <summary>Whether HELLO is answered (RESP3) or rejected (forcing RESP2).</summary>
    public bool SupportsHello { get; set; } = true;

    /// <summary>When set, HELLO ... AUTH and AUTH must carry this password.</summary>
    public string? Password { get; set; }

    public int ConnectionCount => _clients.Count;

    /// <summary>Delay before a command's reply is sent; the connection processes commands in order, like Redis.</summary>
    public Func<FakeCommand, TimeSpan> ReplyDelay { get; set; } = _ => TimeSpan.Zero;

    /// <summary>While set, HELLO is not answered (the client stays in its handshake). Complete it to release.</summary>
    public TaskCompletionSource? HandshakeGate { get; set; }

    public RedisClientOptions CreateOptions(Action<RedisClientOptions>? configure = null)
    {
        var options = new RedisClientOptions { ConnectTimeout = TimeSpan.FromSeconds(5) };
        options.Endpoints.Add(new IPEndPoint(IPAddress.Loopback, Port));
        configure?.Invoke(options);
        return options;
    }

    /// <summary>Closes all client connections (the listener keeps accepting new ones).</summary>
    public void DropConnections()
    {
        foreach (var (id, socket) in _clients)
        {
            socket.Close();
            _clients.TryRemove(id, out _);
        }
    }

    public IEnumerable<FakeCommand> Commands(string name) => Received.Where(c => c.Name == name);

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        DropConnections();
        await _acceptLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync(_stop.Token);
            }
            catch (Exception)
            {
                return;
            }

            var id = Interlocked.Increment(ref _nextId);
            _clients[id] = socket;
            _ = Task.Run(() => ServeAsync(id, socket));
        }
    }

    private async Task ServeAsync(int id, Socket socket)
    {
        await using var stream = new NetworkStream(socket, ownsSocket: true);
        var reader = PipeReader.Create(stream);
        var scanner = new RespScanner(RespLimits.Default);
        try
        {
            while (true)
            {
                var result = await reader.ReadAsync(_stop.Token);
                var buffer = result.Buffer;
                while (scanner.TryScan(buffer, out var length))
                {
                    var frame = RespFrame.Read(buffer.Slice(0, length));
                    var args = new List<string>();
                    foreach (var child in frame.GetChildren())
                    {
                        args.Add(child.GetString());
                    }

                    buffer = buffer.Slice(length);
                    var command = new FakeCommand(id, [.. args]);
                    Received.Enqueue(command);
                    if (command.Name == "HELLO" && HandshakeGate is { } gate)
                    {
                        await gate.Task.WaitAsync(_stop.Token);
                    }

                    var delay = ReplyDelay(command);
                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, _stop.Token);
                    }

                    var reply = Reply(command);
                    if (reply is not null)
                    {
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(reply), _stop.Token);
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted)
                {
                    return;
                }
            }
        }
        catch (Exception)
        {
            // Connection dropped or server stopped.
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    private string? Reply(FakeCommand command) => command.Name switch
    {
        "HELLO" or "AUTH" when Password is not null && !command.Arguments.Contains(Password) => "-WRONGPASS invalid username-password pair or user is disabled.\r\n",
        "HELLO" when SupportsHello => "%2\r\n+server\r\n+redis\r\n+version\r\n+7.4.0\r\n",
        "HELLO" => "-ERR unknown command 'HELLO'\r\n",
        "AUTH" or "SELECT" => "+OK\r\n",
        "PING" => "+PONG\r\n",
        "CLIENT" when command.Arguments.Length > 1 && command.Arguments[1].Equals("SETNAME", StringComparison.OrdinalIgnoreCase) => "+OK\r\n",
        "INFO" => "$22\r\nredis_version:7.4.0\r\n\r\n",
        _ => Handler(command),
    };
}
