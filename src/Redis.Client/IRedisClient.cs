namespace Redis.Client;

/// <summary>Typed command groups plus generic command execution; shared by the client and transactions.</summary>
public interface IRedisCommands
{
    /// <summary>Gets commands that work on keys of any type.</summary>
    IKeyCommands Keys { get; }

    /// <summary>Gets commands for strings.</summary>
    IStringCommands Strings { get; }

    /// <summary>Gets commands for hashes.</summary>
    IHashCommands Hashes { get; }

    /// <summary>Gets commands for lists.</summary>
    IListCommands Lists { get; }

    /// <summary>Gets commands for sets.</summary>
    ISetCommands Sets { get; }

    /// <summary>Gets commands for sorted sets.</summary>
    ISortedSetCommands SortedSets { get; }

    /// <summary>
    /// Sends an arbitrary command, e.g. one without a typed method (streams, modules). Blocking commands such as
    /// <c>BLPOP</c> or <c>XREAD BLOCK</c> are recognized and run on a dedicated connection.
    /// </summary>
    /// <param name="command">The command name, e.g. <c>XADD</c>.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The reply.</returns>
    /// <exception cref="RedisServerException">The server returned an error.</exception>
    /// <exception cref="NotSupportedException">
    /// The command changes connection state (<c>SELECT</c>, <c>MULTI</c>, <c>WATCH</c>, <c>SUBSCRIBE</c>, <c>HELLO</c>,
    /// <c>AUTH</c>, ...) and would affect every caller sharing the connection; use the dedicated APIs instead.
    /// </exception>
    ValueTask<RedisResult> ExecuteAsync(string command, IReadOnlyList<RedisValue>? arguments = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// A thread-safe Redis client. Create one per application (or per Redis server) and share it; it multiplexes all
/// callers over a small number of connections and reconnects automatically.
/// </summary>
public interface IRedisClient : IRedisCommands, IAsyncDisposable
{
    /// <summary>Gets Pub/Sub operations.</summary>
    IRedisSubscriber Subscriber { get; }

    /// <summary>
    /// Connects now instead of on the first command, e.g. to fail fast at startup.
    /// The client keeps retrying in the background after a failure.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting (the connection attempt continues).</param>
    /// <returns>A task that completes once connected.</returns>
    /// <exception cref="RedisConnectionException">The connection attempt failed.</exception>
    ValueTask ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a transaction (<c>MULTI</c>/<c>EXEC</c>). Commands are collected locally and sent as one block when the
    /// transaction is executed; no other command can run in between.
    /// </summary>
    /// <returns>The transaction.</returns>
    IRedisTransaction CreateTransaction();

    /// <summary>
    /// Starts an optimistic transaction: leases a dedicated connection and <c>WATCH</c>es the keys. Read the current
    /// values through <see cref="IRedisWatchedTransaction.Connection"/>, queue writes on the transaction and execute
    /// it; execution returns <see langword="false"/> if a watched key changed in the meantime.
    /// </summary>
    /// <param name="keys">Keys to watch.</param>
    /// <param name="cancellationToken">Cancels leasing the connection and <c>WATCH</c>.</param>
    /// <returns>The transaction; dispose it to return the connection.</returns>
    ValueTask<IRedisWatchedTransaction> BeginWatchedTransactionAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a Lua script with <c>EVALSHA</c>, falling back to <c>EVAL</c> once if the server does not have the script
    /// cached (<c>NOSCRIPT</c>).
    /// </summary>
    /// <param name="script">The script.</param>
    /// <param name="keys">Keys the script accesses (<c>KEYS</c>).</param>
    /// <param name="arguments">Other arguments (<c>ARGV</c>).</param>
    /// <param name="readOnly">Use <c>EVALSHA_RO</c>/<c>EVAL_RO</c> (Redis 7.0+): the script must not write.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The script result.</returns>
    ValueTask<RedisResult> ScriptEvaluateAsync(LuaScript script, IReadOnlyList<RedisKey>? keys = null, IReadOnlyList<RedisValue>? arguments = null, bool readOnly = false, CancellationToken cancellationToken = default);

    /// <summary>Loads a script into the server cache (<c>SCRIPT LOAD</c>), e.g. to warm it up at startup.</summary>
    /// <param name="script">The script.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The SHA1 digest returned by the server.</returns>
    ValueTask<string> ScriptLoadAsync(LuaScript script, CancellationToken cancellationToken = default);
}
