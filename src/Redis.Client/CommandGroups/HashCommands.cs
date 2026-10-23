using System.Runtime.CompilerServices;
using Redis.Client.CommandGroups;
using Redis.Client.Protocol;

namespace Redis.Client;

/// <summary>Commands for hashes.</summary>
public interface IHashCommands
{
    /// <summary>Gets a field (<c>HGET</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="field">The field.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The value, or <see cref="RedisValue.Null"/>.</returns>
    ValueTask<RedisValue> GetAsync(RedisKey key, RedisValue field, CancellationToken cancellationToken = default);

    /// <summary>Gets several fields (<c>HMGET</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="fields">The fields.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>One value per field, <see cref="RedisValue.Null"/> for missing fields.</returns>
    ValueTask<RedisValue[]> GetAsync(RedisKey key, IReadOnlyList<RedisValue> fields, CancellationToken cancellationToken = default);

    /// <summary>Sets a field (<c>HSET</c>, or <c>HSETNX</c> with <see cref="When.NotExists"/>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="when"><see cref="When.NotExists"/> sets the field only if it does not exist.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the field was created.</returns>
    ValueTask<bool> SetAsync(RedisKey key, RedisValue field, RedisValue value, When when = When.Always, CancellationToken cancellationToken = default);

    /// <summary>Sets several fields (<c>HSET</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="fields">Fields and values.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of fields that were created (updated fields are not counted).</returns>
    ValueTask<long> SetAsync(RedisKey key, IReadOnlyList<(RedisValue Field, RedisValue Value)> fields, CancellationToken cancellationToken = default);

    /// <summary>Deletes a field (<c>HDEL</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="field">The field.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the field existed.</returns>
    ValueTask<bool> DeleteAsync(RedisKey key, RedisValue field, CancellationToken cancellationToken = default);

    /// <summary>Deletes fields (<c>HDEL</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="fields">The fields.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of fields that existed.</returns>
    ValueTask<long> DeleteAsync(RedisKey key, IReadOnlyList<RedisValue> fields, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a field exists (<c>HEXISTS</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="field">The field.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns><see langword="true"/> if the field exists.</returns>
    ValueTask<bool> ExistsAsync(RedisKey key, RedisValue field, CancellationToken cancellationToken = default);

    /// <summary>Returns the number of fields (<c>HLEN</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The number of fields.</returns>
    ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Returns the length of a field value in bytes (<c>HSTRLEN</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="field">The field.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The length, or 0 if the field does not exist.</returns>
    ValueTask<long> StringLengthAsync(RedisKey key, RedisValue field, CancellationToken cancellationToken = default);

    /// <summary>Gets all fields and values (<c>HGETALL</c>). For large hashes prefer <see cref="ScanAsync"/>.</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The fields and values; empty if the key does not exist.</returns>
    ValueTask<KeyValuePair<RedisValue, RedisValue>[]> GetAllAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Gets all field names (<c>HKEYS</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The fields.</returns>
    ValueTask<RedisValue[]> KeysAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Gets all values (<c>HVALS</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The values.</returns>
    ValueTask<RedisValue[]> ValuesAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Increments an integer field (<c>HINCRBY</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The increment.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The new value.</returns>
    ValueTask<long> IncrementAsync(RedisKey key, RedisValue field, long value = 1, CancellationToken cancellationToken = default);

    /// <summary>Increments a floating point field (<c>HINCRBYFLOAT</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The increment.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The new value.</returns>
    ValueTask<double> IncrementAsync(RedisKey key, RedisValue field, double value, CancellationToken cancellationToken = default);

    /// <summary>Returns a random field (<c>HRANDFIELD</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The field, or <see cref="RedisValue.Null"/> if the key does not exist.</returns>
    ValueTask<RedisValue> RandomFieldAsync(RedisKey key, CancellationToken cancellationToken = default);

    /// <summary>Returns random fields (<c>HRANDFIELD count</c>); a negative count allows repetitions.</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The number of fields.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The fields.</returns>
    ValueTask<RedisValue[]> RandomFieldsAsync(RedisKey key, long count, CancellationToken cancellationToken = default);

    /// <summary>Returns random fields with their values (<c>HRANDFIELD count WITHVALUES</c>).</summary>
    /// <param name="key">The key.</param>
    /// <param name="count">The number of fields; a negative count allows repetitions.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>The fields and values.</returns>
    ValueTask<KeyValuePair<RedisValue, RedisValue>[]> RandomFieldsWithValuesAsync(RedisKey key, long count, CancellationToken cancellationToken = default);

    /// <summary>Iterates over the fields of a hash (<c>HSCAN</c>) without blocking the server.</summary>
    /// <param name="key">The key.</param>
    /// <param name="pattern">Glob-style pattern for field names (<c>MATCH</c>).</param>
    /// <param name="pageSize">Hint for the amount of work per call (<c>COUNT</c>).</param>
    /// <param name="cancellationToken">Stops the iteration.</param>
    /// <returns>The fields and values.</returns>
    IAsyncEnumerable<KeyValuePair<RedisValue, RedisValue>> ScanAsync(RedisKey key, string? pattern = null, int? pageSize = null, CancellationToken cancellationToken = default);
}

/// <summary>Implementation of <see cref="IHashCommands"/> over a command executor.</summary>
internal sealed class HashCommands(ICommandExecutor executor) : IHashCommands
{
    public ValueTask<RedisValue> GetAsync(RedisKey key, RedisValue field, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HGet, Args.Key(key), field), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue[]> GetAsync(RedisKey key, IReadOnlyList<RedisValue> fields, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HMGet, Args.KeyAndValues(key, fields)), ResultProcessors.Values, cancellationToken);

    public ValueTask<bool> SetAsync(RedisKey key, RedisValue field, RedisValue value, When when = When.Always, CancellationToken cancellationToken = default)
    {
        var metadata = when switch
        {
            When.Always => Commands.HSet,
            When.NotExists => Commands.HSetNx,
            _ => throw new ArgumentOutOfRangeException(nameof(when), when, "HSET supports Always and NotExists."),
        };
        return executor.ExecuteAsync(new RedisCommand(metadata, Args.Key(key), field, value), ResultProcessors.Boolean, cancellationToken);
    }

    public ValueTask<long> SetAsync(RedisKey key, IReadOnlyList<(RedisValue Field, RedisValue Value)> fields, CancellationToken cancellationToken = default)
    {
        Args.NotEmpty(fields, nameof(fields));
        var args = new RedisValue[(fields.Count * 2) + 1];
        args[0] = Args.Key(key);
        for (var i = 0; i < fields.Count; i++)
        {
            args[(2 * i) + 1] = fields[i].Field;
            args[(2 * i) + 2] = fields[i].Value;
        }

        return executor.ExecuteAsync(new RedisCommand(Commands.HSet, args), ResultProcessors.Integer, cancellationToken);
    }

    public ValueTask<bool> DeleteAsync(RedisKey key, RedisValue field, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HDel, Args.Key(key), field), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> DeleteAsync(RedisKey key, IReadOnlyList<RedisValue> fields, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HDel, Args.KeyAndValues(key, fields)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<bool> ExistsAsync(RedisKey key, RedisValue field, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HExists, Args.Key(key), field), ResultProcessors.Boolean, cancellationToken);

    public ValueTask<long> LengthAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HLen, Args.Key(key)), ResultProcessors.Integer, cancellationToken);

    public ValueTask<long> StringLengthAsync(RedisKey key, RedisValue field, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HStrLen, Args.Key(key), field), ResultProcessors.Integer, cancellationToken);

    public ValueTask<KeyValuePair<RedisValue, RedisValue>[]> GetAllAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HGetAll, Args.Key(key)), ResultProcessors.Pairs, cancellationToken);

    public ValueTask<RedisValue[]> KeysAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HKeys, Args.Key(key)), ResultProcessors.Values, cancellationToken);

    public ValueTask<RedisValue[]> ValuesAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HVals, Args.Key(key)), ResultProcessors.Values, cancellationToken);

    public ValueTask<long> IncrementAsync(RedisKey key, RedisValue field, long value = 1, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HIncrBy, Args.Key(key), field, value), ResultProcessors.Integer, cancellationToken);

    public ValueTask<double> IncrementAsync(RedisKey key, RedisValue field, double value, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HIncrByFloat, Args.Key(key), field, value), ResultProcessors.Double, cancellationToken);

    public ValueTask<RedisValue> RandomFieldAsync(RedisKey key, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HRandField, Args.Key(key)), ResultProcessors.Value, cancellationToken);

    public ValueTask<RedisValue[]> RandomFieldsAsync(RedisKey key, long count, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HRandField, Args.Key(key), count), ResultProcessors.Values, cancellationToken);

    public ValueTask<KeyValuePair<RedisValue, RedisValue>[]> RandomFieldsWithValuesAsync(RedisKey key, long count, CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(new RedisCommand(Commands.HRandField, Args.Key(key), count, "WITHVALUES"), ResultProcessors.Pairs, cancellationToken);

    public async IAsyncEnumerable<KeyValuePair<RedisValue, RedisValue>> ScanAsync(RedisKey key, string? pattern = null, int? pageSize = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RedisValue cursor = "0";
        do
        {
            var args = new List<RedisValue> { Args.Key(key), cursor };
            KeyCommands.ScanOptions(args, pattern, pageSize);
            var (next, items) = await executor.ExecuteAsync(new RedisCommand(Commands.HScan, [.. args]), ResultProcessors.Scan, cancellationToken).ConfigureAwait(false);
            for (var i = 0; i + 1 < items.Length; i += 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new(items[i], items[i + 1]);
            }

            cursor = next;
        }
        while ((string?)cursor != "0");
    }
}
