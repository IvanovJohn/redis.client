namespace Redis.Client.Protocol;

/// <summary>A command ready to be written: metadata plus arguments (keys already converted to values).</summary>
internal sealed class RedisCommand
{
    public RedisCommand(CommandMetadata metadata, params RedisValue[] arguments)
    {
        for (var i = 0; i < arguments.Length; i++)
        {
            if (arguments[i].IsNull)
            {
                throw new ArgumentException($"Argument {i} of {metadata.Name} is null; Redis has no null arguments.", nameof(arguments));
            }
        }

        Metadata = metadata;
        Arguments = arguments;
    }

    public CommandMetadata Metadata { get; }

    public RedisValue[] Arguments { get; }

    /// <summary>
    /// The server-side blocking timeout: <see langword="null"/> when the command does not block,
    /// <see cref="Timeout.InfiniteTimeSpan"/> when it blocks forever.
    /// </summary>
    public TimeSpan? GetBlockingTimeout()
    {
        var args = Arguments;
        switch (Metadata.BlockingTimeout)
        {
            case BlockingTimeout.LastArgumentSeconds when args.Length > 0:
                return Seconds(args[^1]);
            case BlockingTimeout.FirstArgumentSeconds when args.Length > 0:
                return Seconds(args[0]);
            case BlockingTimeout.LastArgumentMilliseconds when args.Length > 0:
                return Milliseconds(args[^1]);
            case BlockingTimeout.BlockOptionMilliseconds:
                for (var i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals((string?)args[i], "BLOCK", StringComparison.OrdinalIgnoreCase))
                    {
                        return Milliseconds(args[i + 1]);
                    }
                }

                return null;
            case BlockingTimeout.None:
                return null;
            default:
                return Timeout.InfiniteTimeSpan;
        }
    }

    /// <summary>Whether the command can block and therefore needs a dedicated connection.</summary>
    public bool IsBlocking => Metadata.BlockingTimeout == BlockingTimeout.BlockOptionMilliseconds
        ? GetBlockingTimeout() is not null
        : Metadata.IsBlocking;

    public override string ToString() => Metadata.Name;

    private static TimeSpan Seconds(RedisValue value) =>
        value.TryParse(out double seconds) && seconds > 0 && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : Timeout.InfiniteTimeSpan;

    private static TimeSpan Milliseconds(RedisValue value) =>
        value.TryParse(out long ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : Timeout.InfiniteTimeSpan;
}
