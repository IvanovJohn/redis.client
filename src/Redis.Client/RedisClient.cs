using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Redis.Client.CommandGroups;
using Redis.Client.Connections;
using Redis.Client.Diagnostics;
using Redis.Client.Protocol;
using Redis.Client.PubSub;

namespace Redis.Client;

/// <summary>
/// The Redis client. Thread-safe; create one instance per Redis server and share it for the lifetime of the
/// application. Connections are established lazily (or with <see cref="ConnectAsync"/>) and re-established
/// automatically after failures.
/// </summary>
public sealed class RedisClient : IRedisClient, IDisposable, ICommandExecutor
{
    private readonly RedisClientOptions _options;
    private readonly ILogger _logger;
    private readonly ClientInstrumentation _instrumentation;
    private readonly SingleNodeRouter _router;
    private readonly RedisSubscriber _subscriber;
    private int _disposed;

    /// <summary>Initializes a new instance of the <see cref="RedisClient"/> class from a connection string.</summary>
    /// <param name="connectionString">A <c>redis://</c> or <c>rediss://</c> URI (see <see cref="RedisClientOptions.FromConnectionString"/>).</param>
    public RedisClient(string connectionString)
        : this(RedisClientOptions.FromConnectionString(connectionString))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RedisClient"/> class.</summary>
    /// <param name="options">The settings; a copy is taken, so later changes have no effect.</param>
    /// <param name="loggerFactory">Logger factory; logs are written under the <c>Redis.Client</c> category.</param>
    /// <param name="meterFactory">Meter factory for metrics; a standalone meter is used when omitted.</param>
    /// <exception cref="Microsoft.Extensions.Options.OptionsValidationException">The options are invalid.</exception>
    public RedisClient(RedisClientOptions options, ILoggerFactory? loggerFactory = null, IMeterFactory? meterFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        RedisClientOptionsValidator.ThrowIfInvalid(options);
        _options = options.Clone();
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger("Redis.Client");

        SingleNodeRouter? router = null;
        _instrumentation = new ClientInstrumentation(_options, meterFactory, () => router?.BacklogCount ?? 0, () => router?.PendingCount ?? 0);
        _router = router = new SingleNodeRouter(_options, _logger, _instrumentation);
        _subscriber = new RedisSubscriber(_options, _logger, _instrumentation, this);

        Keys = new KeyCommands(this);
        Strings = new StringCommands(this);
        Hashes = new HashCommands(this);
        Lists = new ListCommands(this);
        Sets = new SetCommands(this);
        SortedSets = new SortedSetCommands(this);
    }

    /// <inheritdoc />
    public IKeyCommands Keys { get; }

    /// <inheritdoc />
    public IStringCommands Strings { get; }

    /// <inheritdoc />
    public IHashCommands Hashes { get; }

    /// <inheritdoc />
    public IListCommands Lists { get; }

    /// <inheritdoc />
    public ISetCommands Sets { get; }

    /// <inheritdoc />
    public ISortedSetCommands SortedSets { get; }

    /// <inheritdoc />
    public IRedisSubscriber Subscriber => _subscriber;

    internal SingleNodeRouter Router => _router;

    internal RedisClientOptions Options => _options;

    /// <inheritdoc />
    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _router.ConnectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask<RedisResult> ExecuteAsync(string command, IReadOnlyList<RedisValue>? arguments = null, CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(CreateGenericCommand(command, arguments), RedisResult.From, cancellationToken);

    /// <inheritdoc />
    public IRedisTransaction CreateTransaction()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new RedisTransaction(this);
    }

    /// <inheritdoc />
    public async ValueTask<IRedisWatchedTransaction> BeginWatchedTransactionAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Args.NotEmpty(keys, nameof(keys));
        var lease = await _router.LeaseDedicatedAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await lease.ExecuteAsync(new RedisCommand(Commands.Watch, Args.Keys(keys)), ResultProcessors.Ok, _options.CommandTimeout, cancellationToken).ConfigureAwait(false);
            return new RedisWatchedTransaction(this, lease);
        }
        catch
        {
            lease.MarkBroken();
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<RedisResult> ScriptEvaluateAsync(LuaScript script, IReadOnlyList<RedisKey>? keys = null, IReadOnlyList<RedisValue>? arguments = null, bool readOnly = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        try
        {
            var evalSha = new RedisCommand(readOnly ? Commands.EvalShaRo : Commands.EvalSha, ScriptArgs(script.Sha1, keys, arguments));
            return await ExecuteCoreAsync(evalSha, RedisResult.From, cancellationToken).ConfigureAwait(false);
        }
        catch (RedisServerException ex) when (ex.ErrorCode == "NOSCRIPT")
        {
            // EVAL runs the script and caches it, so the next EVALSHA succeeds.
            var eval = new RedisCommand(readOnly ? Commands.EvalRo : Commands.Eval, ScriptArgs(script.Source, keys, arguments));
            return await ExecuteCoreAsync(eval, RedisResult.From, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask<string> ScriptLoadAsync(LuaScript script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        return ExecuteCoreAsync(new RedisCommand(Commands.Script, "LOAD", script.Source), static f => f.GetString(), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _subscriber.DisposeAsync().ConfigureAwait(false);
        await _router.DisposeAsync().ConfigureAwait(false);
        _instrumentation.Dispose();
    }

    /// <summary>
    /// Disposes the client synchronously (for containers that dispose synchronously); prefer <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    ValueTask<T> ICommandExecutor.ExecuteAsync<T>(RedisCommand command, Func<RespFrame, T> processor, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(command, processor, cancellationToken);

    /// <summary>Builds a command for <see cref="ExecuteAsync"/>, rejecting commands that change connection state.</summary>
    internal static RedisCommand CreateGenericCommand(string command, IReadOnlyList<RedisValue>? arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var metadata = Commands.Lookup(command);
        var args = arguments is null ? [] : arguments.ToArray();
        if (metadata.ChangesConnectionState || IsClientReply(metadata, args))
        {
            throw new NotSupportedException(
                $"{metadata.Name} changes the state of the connection, which is shared by all callers. " +
                "Use CreateTransaction/BeginWatchedTransactionAsync for transactions, Subscriber for Pub/Sub and RedisClientOptions for the database, credentials and protocol.");
        }

        return new RedisCommand(metadata, args);
    }

    /// <summary>Runs a command through the router with tracing and metrics.</summary>
    internal ValueTask<T> ExecuteCoreAsync<T>(RedisCommand command, Func<RespFrame, T> processor, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return ValueTask.FromException<T>(new ObjectDisposedException(nameof(RedisClient)));
        }

        if (!ClientInstrumentation.ActivitySource.HasListeners() && !_instrumentation.IsDurationEnabled)
        {
            return new ValueTask<T>(_router.ExecuteAsync(command, processor, cancellationToken));
        }

        return Instrumented(command.Metadata.Name, command, () => _router.ExecuteAsync(command, processor, cancellationToken));
    }

    /// <summary>Wraps an operation in an activity and a duration measurement.</summary>
    internal async ValueTask<T> Instrumented<T>(string operation, RedisCommand? command, Func<Task<T>> run, int batchSize = 0)
    {
        using var activity = StartActivity(operation, command, batchSize);
        var started = Stopwatch.GetTimestamp();
        string? errorType = null;
        try
        {
            var result = await run().ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            errorType = ErrorType(ex);
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error);
                activity.SetTag("error.type", errorType);
            }

            throw;
        }
        finally
        {
            if (_instrumentation.IsDurationEnabled)
            {
                _instrumentation.RecordDuration(operation, Stopwatch.GetElapsedTime(started), errorType);
            }
        }
    }

    internal bool IsInstrumented => ClientInstrumentation.ActivitySource.HasListeners() || _instrumentation.IsDurationEnabled;

    internal static string ErrorType(Exception exception) => exception switch
    {
        RedisServerException server => server.ErrorCode,
        _ => exception.GetType().FullName ?? exception.GetType().Name,
    };

    /// <summary>Command text for <c>db.query.text</c>; credentials are always masked.</summary>
    internal static string FormatCommand(RedisCommand command)
    {
        const int MaxArgumentLength = 128;
        var builder = new StringBuilder(command.Metadata.Name);
        var masked = command.Metadata == Commands.Auth || command.Metadata == Commands.Hello;
        for (var i = 0; i < command.Arguments.Length; i++)
        {
            builder.Append(' ');
            if (masked && !(command.Metadata == Commands.Hello && i == 0))
            {
                builder.Append('?');
                continue;
            }

            var text = (string?)command.Arguments[i] ?? string.Empty;
            builder.Append(text.Length > MaxArgumentLength ? text[..MaxArgumentLength] + "…" : text);
        }

        return builder.ToString();
    }

    private static bool IsClientReply(CommandMetadata metadata, RedisValue[] args) =>
        metadata == Commands.Client && args.Length > 0 && string.Equals((string?)args[0], "REPLY", StringComparison.OrdinalIgnoreCase);

    private static RedisValue[] ScriptArgs(RedisValue scriptOrSha, IReadOnlyList<RedisKey>? keys, IReadOnlyList<RedisValue>? arguments)
    {
        var keyCount = keys?.Count ?? 0;
        var args = new RedisValue[2 + keyCount + (arguments?.Count ?? 0)];
        args[0] = scriptOrSha;
        args[1] = keyCount;
        for (var i = 0; i < keyCount; i++)
        {
            args[2 + i] = Args.Key(keys![i]);
        }

        for (var i = 0; i < (arguments?.Count ?? 0); i++)
        {
            args[2 + keyCount + i] = arguments![i];
        }

        return args;
    }

    private Activity? StartActivity(string operation, RedisCommand? command, int batchSize)
    {
        var activity = ClientInstrumentation.ActivitySource.StartActivity(operation, ActivityKind.Client);
        if (activity is null || !activity.IsAllDataRequested)
        {
            return activity;
        }

        activity.SetTag("db.system.name", "redis");
        activity.SetTag("db.operation.name", operation);
        activity.SetTag("db.namespace", _instrumentation.Namespace);
        activity.SetTag("server.address", _instrumentation.ServerAddress);
        activity.SetTag("server.port", _instrumentation.ServerPort);
        if (batchSize > 0)
        {
            activity.SetTag("db.operation.batch.size", batchSize);
        }

        if (_instrumentation.RecordArguments && command is not null)
        {
            activity.SetTag("db.query.text", FormatCommand(command));
        }

        return activity;
    }
}
