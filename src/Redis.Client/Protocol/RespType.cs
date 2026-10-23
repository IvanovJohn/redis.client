namespace Redis.Client.Protocol;

/// <summary>RESP2/RESP3 frame types. RESP2 null bulk strings and null arrays are normalized to <see cref="Null"/>.</summary>
internal enum RespType : byte
{
    SimpleString = (byte)'+',
    Error = (byte)'-',
    Integer = (byte)':',
    BulkString = (byte)'$',
    Array = (byte)'*',
    Null = (byte)'_',
    Boolean = (byte)'#',
    Double = (byte)',',
    BigNumber = (byte)'(',
    BulkError = (byte)'!',
    VerbatimString = (byte)'=',
    Map = (byte)'%',
    Set = (byte)'~',
    Push = (byte)'>',
    Attribute = (byte)'|',
}

/// <summary>Protection limits applied while parsing replies.</summary>
internal readonly record struct RespLimits(long MaxBulkLength, int MaxNestingDepth)
{
    public static RespLimits Default { get; } = new(512L * 1024 * 1024, 64);
}
