using System.Runtime.CompilerServices;
using Redis.Client.Protocol;

namespace Redis.Client;

/// <summary>Commands that work on keys of any type.</summary>
public interface IKeyCommands
{
    /// <summary>Deletes a key (<c>DEL</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the key existed.</returns>
    ValueTask<bool> DeleteAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Deletes keys (<c>DEL</c>).</summary>
    /// <param name="keys">The keys.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of keys that existed.</returns>
    ValueTask<long> DeleteAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default);

    /// <summary>Deletes a key and reclaims its memory in the background (<c>UNLINK</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the key existed.</returns>
    ValueTask<bool> UnlinkAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Deletes keys and reclaims their memory in the background (<c>UNLINK</c>).</summary>
    /// <param name="keys">The keys.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of keys that existed.</returns>
    ValueTask<long> UnlinkAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a key exists (<c>EXISTS</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the key exists.</returns>
    ValueTask<bool> ExistsAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Counts existing keys (<c>EXISTS</c>); a key listed twice is counted twice.</summary>
    /// <param name="keys">The keys.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of existing keys.</returns>
    ValueTask<long> ExistsAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default);

    /// <summary>Sets a relative expiry (<c>EXPIRE</c> for whole seconds, otherwise <c>PEXPIRE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="expiry">Time until the key is deleted.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the key exists and the expiry was set.</returns>
    ValueTask<bool> ExpireAsync(RedisKey key, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>Sets an absolute expiry (<c>EXPIREAT</c> for whole seconds, otherwise <c>PEXPIREAT</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="expireAt">When the key is deleted.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the key exists and the expiry was set.</returns>
    ValueTask<bool> ExpireAtAsync(RedisKey key, DateTimeOffset expireAt, CancellationToken cancellationToken = default);

    /// <summary>Returns the remaining time to live (<c>PTTL</c>); distinguishes keys without expiry from missing keys.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The time to live.</returns>
    ValueTask<TimeToLive> TimeToLiveAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Removes the expiry (<c>PERSIST</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if an expiry was removed.</returns>
    ValueTask<bool> PersistAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Returns the type of the value stored at a key (<c>TYPE</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The type, or <see cref="RedisType.None"/> if the key does not exist.</returns>
    ValueTask<RedisType> TypeAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Renames a key (<c>RENAME</c>, or <c>RENAMENX</c> with <see cref="When.NotExists"/>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="newKey">The new name.</param>
    /// <param name="when"><see cref="When.NotExists"/> renames only if <paramref name="newKey"/> does not exist.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the key was renamed.</returns>
    /// <exception cref="RedisServerException">The key does not exist.</exception>
    ValueTask<bool> RenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CancellationToken cancellationToken = default);

    /// <summary>Copies a value to another key (<c>COPY</c>).</summary>
    /// <param name="source">The source key.</param>
    /// <param name="destination">The destination key.</param>
    /// <param name="replace">Overwrite an existing destination.</param>
    /// <param name="destinationDatabase">Copy into another database.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the value was copied.</returns>
    ValueTask<bool> CopyAsync(RedisKey source, RedisKey destination, bool replace = false, int? destinationDatabase = null, CancellationToken cancellationToken = default);

    /// <summary>Updates the last access time of a key (<c>TOUCH</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the key exists.</returns>
    ValueTask<bool> TouchAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Updates the last access time of keys (<c>TOUCH</c>).</summary>
    /// <param name="keys">The keys.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of existing keys.</returns>
    ValueTask<long> TouchAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Iterates over the keys of the database (<c>SCAN</c>) without blocking the server. Keys may be returned more
    /// than once and keys changed during the iteration may be missed, as documented for <c>SCAN</c>.
    /// </summary>
    /// <param name="pattern">Glob-style pattern (<c>MATCH</c>).</param>
    /// <param name="pageSize">Hint for the amount of work per call (<c>COUNT</c>).</param>
    /// <param name="type">Only keys of this type (<c>TYPE</c>).</param>
    /// <param name="cancellationToken">Stops the iteration.</param>
    /// <returns>The keys.</returns>
    IAsyncEnumerable<RedisKey> ScanAsync(string? pattern = null, int? pageSize = null, RedisType? type = null, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="IKeyCommands"/> over a command executor.</summary>
internal sealed class KeyCommands(CommandGroups.ICommandExecutor executor) : IKeyCommands
{
    public ValueTask<bool> DeleteAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Del, CommandGroups.Args.Key(key)), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> DeleteAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default) =>
        MultiKey(Commands.Del, keys, cancellationToken);

    public ValueTask<bool> UnlinkAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Unlink, CommandGroups.Args.Key(key)), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> UnlinkAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default) =>
        MultiKey(Commands.Unlink, keys, cancellationToken);

    public ValueTask<bool> ExistsAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Exists, CommandGroups.Args.Key(key)), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> ExistsAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default) =>
        MultiKey(Commands.Exists, keys, cancellationToken);

    public ValueTask<bool> ExpireAsync(RedisKey key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var command = expiry.Ticks % TimeSpan.TicksPerSecond == 0
            ? new RedisCommand(Commands.Expire, CommandGroups.Args.Key(key), (long)expiry.TotalSeconds)
            : new RedisCommand(Commands.PExpire, CommandGroups.Args.Key(key), (long)Math.Ceiling(expiry.TotalMilliseconds));
        return executor.ExecuteAsync(command, ResultProcessors.Boolean, cancellationToken);
    }

    public ValueTask<bool> ExpireAtAsync(RedisKey key, DateTimeOffset expireAt, CancellationToken cancellationToken = default)
    {
        var ms = expireAt.ToUnixTimeMilliseconds();
        var command = ms % 1000 == 0
            ? new RedisCommand(Commands.ExpireAt, CommandGroups.Args.Key(key), ms / 1000)
            : new RedisCommand(Commands.PExpireAt, CommandGroups.Args.Key(key), ms);
        return executor.ExecuteAsync(command, ResultProcessors.Boolean, cancellationToken);
    }

    public ValueTask<TimeToLive> TimeToLiveAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.PTtl, CommandGroups.Args.Key(key)), ResultProcessors.TimeToLive, cancellationToken);

    public ValueTask<bool> PersistAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Persist, CommandGroups.Args.Key(key)), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<RedisType> TypeAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Type, CommandGroups.Args.Key(key)), ResultProcessors.Type, cancellationToken);

    public ValueTask<bool> RenameAsync(RedisKey key, RedisKey newKey, When when = When.Always, CancellationToken cancellationToken = default)
    {
        var metadata = when switch
        {
            When.Always => Commands.Rename,
            When.NotExists => Commands.RenameNx,
            _ => throw new ArgumentOutOfRangeException(nameof(when), when, "RENAME supports Always and NotExists."),
        };
        return executor.ExecuteAsync(new RedisCommand(metadata, CommandGroups.Args.Key(key), CommandGroups.Args.Key(newKey)), ResultProcessors.Boolean, cancellationToken);
    }

    public ValueTask<bool> CopyAsync(RedisKey source, RedisKey destination, bool replace = false, int? destinationDatabase = null, CancellationToken cancellationToken = default)
    {
        var args = new List<RedisValue> { CommandGroups.Args.Key(source), CommandGroups.Args.Key(destination) };
        if (destinationDatabase is { } db)
        {
            args.Add("DB");
            args.Add(db);
        }

        if (replace)
        {
            args.Add("REPLACE");
        }

        return executor.ExecuteAsync(new RedisCommand(Commands.Copy, [.. args]), ResultProcessors.Boolean, cancellationToken);
    }

    public ValueTask<bool> TouchAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.Touch, CommandGroups.Args.Key(key)), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> TouchAsync(IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken = default) =>
        MultiKey(Commands.Touch, keys, cancellationToken);

    public async IAsyncEnumerable<RedisKey> ScanAsync(string? pattern = null, int? pageSize = null, RedisType? type = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RedisValue cursor = "0";
        do
        {
            var args = new List<RedisValue> { cursor };
            ScanOptions(args, pattern, pageSize);
            if (type is { } t)
            {
                args.Add("TYPE");
                args.Add(TypeName(t));
            }

            var (next, items) = await executor.ExecuteAsync(new RedisCommand(Commands.Scan, [.. args]), ResultProcessors.Scan, cancellationToken).ConfigureAwait(false);
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (byte[])item!;
            }

            cursor = next;
        }
        while ((string?)cursor != "0");
    }

    internal static void ScanOptions(List<RedisValue> args, string? pattern, int? pageSize)
    {
        if (pattern is not null)
        {
            args.Add("MATCH");
            args.Add(pattern);
        }

        if (pageSize is { } count)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1, nameof(pageSize));
            args.Add("COUNT");
            args.Add(count);
        }
    }

    private static string TypeName(RedisType type) => type switch
    {
        RedisType.String => "string",
        RedisType.List => "list",
        RedisType.Set => "set",
        RedisType.SortedSet => "zset",
        RedisType.Hash => "hash",
        RedisType.Stream => "stream",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Only concrete data types can be scanned for."),
    };

    private ValueTask<long> MultiKey(CommandMetadata metadata, IReadOnlyList<RedisKey> keys, CancellationToken cancellationToken)
    {
        CommandGroups.Args.NotEmpty(keys, nameof(keys));
        return executor.ExecuteAsync(new RedisCommand(metadata, CommandGroups.Args.Keys(keys)), ResultProcessors.Integer, cancellationToken);
    }
}
