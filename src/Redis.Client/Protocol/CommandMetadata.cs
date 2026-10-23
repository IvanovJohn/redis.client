using System.Text;

namespace Redis.Client.Protocol;

[Flags]
internal enum CommandFlags
{
    None = 0,

    /// <summary>The command does not modify data.</summary>
    ReadOnly = 1 << 0,

    /// <summary>The command can block the connection; it must run on a dedicated connection.</summary>
    Blocking = 1 << 1,

    /// <summary>The command has no key arguments.</summary>
    NoKey = 1 << 2,

    /// <summary>
    /// The command changes per-connection state (database, transaction, subscription, protocol, authentication).
    /// It must never be sent on a shared multiplexed connection by user code.
    /// </summary>
    ConnectionState = 1 << 3,
}

/// <summary>Where a blocking command carries its server-side timeout.</summary>
internal enum BlockingTimeout
{
    None,

    /// <summary>Last argument, in seconds (may be fractional); <c>0</c> means forever.</summary>
    LastArgumentSeconds,

    /// <summary>First argument, in seconds (may be fractional); <c>0</c> means forever.</summary>
    FirstArgumentSeconds,

    /// <summary>Last argument, in milliseconds; <c>0</c> means forever.</summary>
    LastArgumentMilliseconds,

    /// <summary>Value after the <c>BLOCK</c> option, in milliseconds; the command blocks only if the option is present.</summary>
    BlockOptionMilliseconds,
}

/// <summary>
/// Static description of a command: its name, flags and key positions. Key positions follow the
/// <c>COMMAND INFO</c> convention (first key, last key, step) relative to the argument list and are kept for
/// future cluster routing.
/// </summary>
internal sealed class CommandMetadata
{
    public CommandMetadata(string name, CommandFlags flags, int firstKey = 0, int lastKey = 0, int keyStep = 1, BlockingTimeout blocking = BlockingTimeout.None)
    {
        Name = name;
        NameBytes = Encoding.ASCII.GetBytes(name);
        Flags = blocking == BlockingTimeout.None ? flags : flags | CommandFlags.Blocking;
        FirstKey = firstKey;
        LastKey = lastKey;
        KeyStep = keyStep;
        BlockingTimeout = blocking;
    }

    public string Name { get; }

    public byte[] NameBytes { get; }

    public CommandFlags Flags { get; }

    /// <summary>1-based index of the first key in the arguments; 0 when the command has no keys.</summary>
    public int FirstKey { get; }

    /// <summary>1-based index of the last key; negative values count from the end (-1 is the last argument).</summary>
    public int LastKey { get; }

    public int KeyStep { get; }

    public BlockingTimeout BlockingTimeout { get; }

    public bool IsBlocking => (Flags & CommandFlags.Blocking) != 0;

    public bool ChangesConnectionState => (Flags & CommandFlags.ConnectionState) != 0;

    public override string ToString() => Name;
}
