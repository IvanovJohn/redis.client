using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Redis.Client.Diagnostics;

namespace Redis.Client.PubSub;

/// <summary>A subscription held by application code; several may share one server subscription.</summary>
internal sealed class LocalSubscription : IRedisSubscription
{
    private readonly RedisSubscriber _owner;
    private readonly Channel<RedisMessage> _buffer;
    private readonly bool _wait;
    private readonly CancellationTokenSource _stop = new();
    private Task _pump = Task.CompletedTask;
    private long _dropped;
    private int _disposed;

    public LocalSubscription(RedisSubscriber owner, string name, bool isPattern, RedisClientOptions options, ClientInstrumentation instrumentation)
    {
        _owner = owner;
        Name = name;
        IsPattern = isPattern;
        _wait = options.SubscriptionOverflowPolicy == SubscriptionOverflowPolicy.Wait;
        var channelOptions = new BoundedChannelOptions(options.SubscriptionBufferSize)
        {
            FullMode = _wait ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
        };
        _buffer = Channel.CreateBounded<RedisMessage>(channelOptions, _ =>
        {
            Interlocked.Increment(ref _dropped);
            instrumentation.PubSubDropped(name);
        });
    }

    public event EventHandler? ConnectionLost;

    public event EventHandler? ConnectionRestored;

    public string Name { get; }

    public bool IsPattern { get; }

    public ChannelReader<RedisMessage> Messages => _buffer.Reader;

    public long DroppedMessages => Interlocked.Read(ref _dropped);

    /// <summary>Starts calling <paramref name="handler"/> for each message.</summary>
    public void StartHandler(Func<RedisMessage, CancellationToken, ValueTask> handler, ILogger logger)
    {
        var token = _stop.Token;
        _pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var message in _buffer.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    try
                    {
                        await handler(message, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.SubscriptionHandlerFailed(logger, message.Channel, ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>Delivers a message. Returns a pending task only under <see cref="SubscriptionOverflowPolicy.Wait"/>.</summary>
    public ValueTask DeliverAsync(RedisMessage message, CancellationToken cancellationToken)
    {
        if (_buffer.Writer.TryWrite(message) || !_wait)
        {
            return ValueTask.CompletedTask;
        }

        return WaitAndWriteAsync(message, cancellationToken);
    }

    public void OnConnectionLost() => ConnectionLost?.Invoke(this, EventArgs.Empty);

    public void OnConnectionRestored() => ConnectionRestored?.Invoke(this, EventArgs.Empty);

    /// <summary>Ends the message stream without unsubscribing (client shutdown).</summary>
    public void Complete() => _buffer.Writer.TryComplete();

    public IAsyncEnumerator<RedisMessage> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
        _buffer.Reader.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _owner.UnsubscribeAsync(this).ConfigureAwait(false);
        _buffer.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        await _pump.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stop.Dispose();
    }

    private async ValueTask WaitAndWriteAsync(RedisMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await _buffer.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
        }
    }
}
