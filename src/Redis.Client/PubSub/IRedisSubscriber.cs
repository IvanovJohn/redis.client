using System.Threading.Channels;

namespace Redis.Client;

/// <summary>A message received from a Pub/Sub channel.</summary>
/// <param name="Channel">The channel the message was published to.</param>
/// <param name="Value">The message payload.</param>
/// <param name="Pattern">The pattern that matched, for pattern subscriptions; otherwise <see langword="null"/>.</param>
public readonly record struct RedisMessage(string Channel, RedisValue Value, string? Pattern);

/// <summary>
/// Pub/Sub. Subscriptions share one dedicated connection, created on the first subscription, and are restored
/// automatically after a reconnect. Messages published while the connection is down are lost, as in Redis.
/// </summary>
public interface IRedisSubscriber
{
    /// <summary>Subscribes to a channel (<c>SUBSCRIBE</c>); read messages from the returned subscription.</summary>
    /// <param name="channel">The channel name.</param>
    /// <param name="cancellationToken">Cancels waiting for the server confirmation.</param>
    /// <returns>The subscription; dispose it to unsubscribe.</returns>
    ValueTask<IRedisSubscription> SubscribeAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to a channel and calls <paramref name="handler"/> for every message, one at a time.</summary>
    /// <param name="channel">The channel name.</param>
    /// <param name="handler">Message handler; exceptions are logged and do not stop delivery.</param>
    /// <param name="cancellationToken">Cancels waiting for the server confirmation.</param>
    /// <returns>The subscription; dispose it to unsubscribe.</returns>
    ValueTask<IRedisSubscription> SubscribeAsync(string channel, Func<RedisMessage, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to channels matching a glob-style pattern (<c>PSUBSCRIBE</c>).</summary>
    /// <param name="pattern">The pattern, e.g. <c>orders.*</c>.</param>
    /// <param name="cancellationToken">Cancels waiting for the server confirmation.</param>
    /// <returns>The subscription; dispose it to unsubscribe.</returns>
    ValueTask<IRedisSubscription> SubscribePatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to a pattern and calls <paramref name="handler"/> for every message, one at a time.</summary>
    /// <param name="pattern">The pattern.</param>
    /// <param name="handler">Message handler; exceptions are logged and do not stop delivery.</param>
    /// <param name="cancellationToken">Cancels waiting for the server confirmation.</param>
    /// <returns>The subscription; dispose it to unsubscribe.</returns>
    ValueTask<IRedisSubscription> SubscribePatternAsync(string pattern, Func<RedisMessage, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default);

    /// <summary>Publishes a message (<c>PUBLISH</c>) over the shared multiplexed connection.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of clients that received the message.</returns>
    ValueTask<long> PublishAsync(string channel, RedisValue message, CancellationToken cancellationToken = default);
}

/// <summary>
/// One local subscription with its own bounded message buffer
/// (<see cref="RedisClientOptions.SubscriptionBufferSize"/>). Dispose it to unsubscribe.
/// </summary>
public interface IRedisSubscription : IAsyncEnumerable<RedisMessage>, IAsyncDisposable
{
    /// <summary>Gets the channel or pattern.</summary>
    string Name { get; }

    /// <summary>Gets a value indicating whether <see cref="Name"/> is a pattern.</summary>
    bool IsPattern { get; }

    /// <summary>Gets the buffered messages. Not used by handler-based subscriptions.</summary>
    ChannelReader<RedisMessage> Messages { get; }

    /// <summary>Gets the number of messages dropped because the buffer was full.</summary>
    long DroppedMessages { get; }

    /// <summary>Raised when the subscription connection is lost; messages published until it is restored are missed.</summary>
    event EventHandler? ConnectionLost;

    /// <summary>Raised when the connection is back and the subscription has been restored.</summary>
    event EventHandler? ConnectionRestored;
}
