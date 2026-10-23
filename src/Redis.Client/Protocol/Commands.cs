using System.Collections.Frozen;

namespace Redis.Client.Protocol;

/// <summary>Metadata of the commands the client knows about. Unknown commands get neutral metadata.</summary>
internal static class Commands
{
    private const CommandFlags R = CommandFlags.ReadOnly;
    private const CommandFlags W = CommandFlags.None;
    private const CommandFlags NoKey = CommandFlags.NoKey;
    private const CommandFlags State = CommandFlags.ConnectionState | CommandFlags.NoKey;

    // Connection and server
    public static readonly CommandMetadata Ping = new("PING", R | NoKey);
    public static readonly CommandMetadata Echo = new("ECHO", R | NoKey);
    public static readonly CommandMetadata Hello = new("HELLO", State);
    public static readonly CommandMetadata Auth = new("AUTH", State);
    public static readonly CommandMetadata Select = new("SELECT", State);
    public static readonly CommandMetadata Client = new("CLIENT", NoKey);
    public static readonly CommandMetadata Info = new("INFO", R | NoKey);
    public static readonly CommandMetadata Quit = new("QUIT", State);
    public static readonly CommandMetadata Reset = new("RESET", State);
    public static readonly CommandMetadata Monitor = new("MONITOR", State);
    public static readonly CommandMetadata Wait = new("WAIT", NoKey, blocking: BlockingTimeout.LastArgumentMilliseconds);
    public static readonly CommandMetadata WaitAof = new("WAITAOF", NoKey, blocking: BlockingTimeout.LastArgumentMilliseconds);

    // Transactions
    public static readonly CommandMetadata Multi = new("MULTI", State);
    public static readonly CommandMetadata Exec = new("EXEC", State);
    public static readonly CommandMetadata Discard = new("DISCARD", State);
    public static readonly CommandMetadata Watch = new("WATCH", CommandFlags.ConnectionState, 1, -1);
    public static readonly CommandMetadata Unwatch = new("UNWATCH", State);

    // Scripting
    public static readonly CommandMetadata Eval = new("EVAL", W);
    public static readonly CommandMetadata EvalSha = new("EVALSHA", W);
    public static readonly CommandMetadata EvalRo = new("EVAL_RO", R);
    public static readonly CommandMetadata EvalShaRo = new("EVALSHA_RO", R);
    public static readonly CommandMetadata Script = new("SCRIPT", NoKey);

    // Pub/Sub
    public static readonly CommandMetadata Subscribe = new("SUBSCRIBE", State);
    public static readonly CommandMetadata Unsubscribe = new("UNSUBSCRIBE", State);
    public static readonly CommandMetadata PSubscribe = new("PSUBSCRIBE", State);
    public static readonly CommandMetadata PUnsubscribe = new("PUNSUBSCRIBE", State);
    public static readonly CommandMetadata SSubscribe = new("SSUBSCRIBE", State);
    public static readonly CommandMetadata SUnsubscribe = new("SUNSUBSCRIBE", State);
    public static readonly CommandMetadata Publish = new("PUBLISH", NoKey);

    // Keys
    public static readonly CommandMetadata Del = new("DEL", W, 1, -1);
    public static readonly CommandMetadata Unlink = new("UNLINK", W, 1, -1);
    public static readonly CommandMetadata Exists = new("EXISTS", R, 1, -1);
    public static readonly CommandMetadata Expire = new("EXPIRE", W, 1, 1);
    public static readonly CommandMetadata PExpire = new("PEXPIRE", W, 1, 1);
    public static readonly CommandMetadata ExpireAt = new("EXPIREAT", W, 1, 1);
    public static readonly CommandMetadata PExpireAt = new("PEXPIREAT", W, 1, 1);
    public static readonly CommandMetadata Ttl = new("TTL", R, 1, 1);
    public static readonly CommandMetadata PTtl = new("PTTL", R, 1, 1);
    public static readonly CommandMetadata Persist = new("PERSIST", W, 1, 1);
    public static readonly CommandMetadata Type = new("TYPE", R, 1, 1);
    public static readonly CommandMetadata Rename = new("RENAME", W, 1, 2);
    public static readonly CommandMetadata RenameNx = new("RENAMENX", W, 1, 2);
    public static readonly CommandMetadata Copy = new("COPY", W, 1, 2);
    public static readonly CommandMetadata Touch = new("TOUCH", R, 1, -1);
    public static readonly CommandMetadata Scan = new("SCAN", R | NoKey);

    // Strings
    public static readonly CommandMetadata Get = new("GET", R, 1, 1);
    public static readonly CommandMetadata Set = new("SET", W, 1, 1);
    public static readonly CommandMetadata MGet = new("MGET", R, 1, -1);
    public static readonly CommandMetadata MSet = new("MSET", W, 1, -1, 2);
    public static readonly CommandMetadata MSetNx = new("MSETNX", W, 1, -1, 2);
    public static readonly CommandMetadata GetDel = new("GETDEL", W, 1, 1);
    public static readonly CommandMetadata GetEx = new("GETEX", W, 1, 1);
    public static readonly CommandMetadata GetRange = new("GETRANGE", R, 1, 1);
    public static readonly CommandMetadata SetRange = new("SETRANGE", W, 1, 1);
    public static readonly CommandMetadata Append = new("APPEND", W, 1, 1);
    public static readonly CommandMetadata StrLen = new("STRLEN", R, 1, 1);
    public static readonly CommandMetadata Incr = new("INCR", W, 1, 1);
    public static readonly CommandMetadata IncrBy = new("INCRBY", W, 1, 1);
    public static readonly CommandMetadata IncrByFloat = new("INCRBYFLOAT", W, 1, 1);
    public static readonly CommandMetadata Decr = new("DECR", W, 1, 1);
    public static readonly CommandMetadata DecrBy = new("DECRBY", W, 1, 1);

    // Hashes
    public static readonly CommandMetadata HGet = new("HGET", R, 1, 1);
    public static readonly CommandMetadata HMGet = new("HMGET", R, 1, 1);
    public static readonly CommandMetadata HSet = new("HSET", W, 1, 1);
    public static readonly CommandMetadata HSetNx = new("HSETNX", W, 1, 1);
    public static readonly CommandMetadata HDel = new("HDEL", W, 1, 1);
    public static readonly CommandMetadata HExists = new("HEXISTS", R, 1, 1);
    public static readonly CommandMetadata HLen = new("HLEN", R, 1, 1);
    public static readonly CommandMetadata HStrLen = new("HSTRLEN", R, 1, 1);
    public static readonly CommandMetadata HGetAll = new("HGETALL", R, 1, 1);
    public static readonly CommandMetadata HKeys = new("HKEYS", R, 1, 1);
    public static readonly CommandMetadata HVals = new("HVALS", R, 1, 1);
    public static readonly CommandMetadata HIncrBy = new("HINCRBY", W, 1, 1);
    public static readonly CommandMetadata HIncrByFloat = new("HINCRBYFLOAT", W, 1, 1);
    public static readonly CommandMetadata HRandField = new("HRANDFIELD", R, 1, 1);
    public static readonly CommandMetadata HScan = new("HSCAN", R, 1, 1);

    // Lists
    public static readonly CommandMetadata LPush = new("LPUSH", W, 1, 1);
    public static readonly CommandMetadata RPush = new("RPUSH", W, 1, 1);
    public static readonly CommandMetadata LPushX = new("LPUSHX", W, 1, 1);
    public static readonly CommandMetadata RPushX = new("RPUSHX", W, 1, 1);
    public static readonly CommandMetadata LPop = new("LPOP", W, 1, 1);
    public static readonly CommandMetadata RPop = new("RPOP", W, 1, 1);
    public static readonly CommandMetadata LRange = new("LRANGE", R, 1, 1);
    public static readonly CommandMetadata LLen = new("LLEN", R, 1, 1);
    public static readonly CommandMetadata LIndex = new("LINDEX", R, 1, 1);
    public static readonly CommandMetadata LSet = new("LSET", W, 1, 1);
    public static readonly CommandMetadata LInsert = new("LINSERT", W, 1, 1);
    public static readonly CommandMetadata LRem = new("LREM", W, 1, 1);
    public static readonly CommandMetadata LTrim = new("LTRIM", W, 1, 1);
    public static readonly CommandMetadata LPos = new("LPOS", R, 1, 1);
    public static readonly CommandMetadata LMove = new("LMOVE", W, 1, 2);
    public static readonly CommandMetadata BLPop = new("BLPOP", W, 1, -2, blocking: BlockingTimeout.LastArgumentSeconds);
    public static readonly CommandMetadata BRPop = new("BRPOP", W, 1, -2, blocking: BlockingTimeout.LastArgumentSeconds);
    public static readonly CommandMetadata BLMove = new("BLMOVE", W, 1, 2, blocking: BlockingTimeout.LastArgumentSeconds);
    public static readonly CommandMetadata BRPopLPush = new("BRPOPLPUSH", W, 1, 2, blocking: BlockingTimeout.LastArgumentSeconds);
    public static readonly CommandMetadata BLMPop = new("BLMPOP", W, blocking: BlockingTimeout.FirstArgumentSeconds);

    // Sets
    public static readonly CommandMetadata SAdd = new("SADD", W, 1, 1);
    public static readonly CommandMetadata SRem = new("SREM", W, 1, 1);
    public static readonly CommandMetadata SMembers = new("SMEMBERS", R, 1, 1);
    public static readonly CommandMetadata SIsMember = new("SISMEMBER", R, 1, 1);
    public static readonly CommandMetadata SMIsMember = new("SMISMEMBER", R, 1, 1);
    public static readonly CommandMetadata SCard = new("SCARD", R, 1, 1);
    public static readonly CommandMetadata SPop = new("SPOP", W, 1, 1);
    public static readonly CommandMetadata SRandMember = new("SRANDMEMBER", R, 1, 1);
    public static readonly CommandMetadata SMove = new("SMOVE", W, 1, 2);
    public static readonly CommandMetadata SInter = new("SINTER", R, 1, -1);
    public static readonly CommandMetadata SUnion = new("SUNION", R, 1, -1);
    public static readonly CommandMetadata SDiff = new("SDIFF", R, 1, -1);
    public static readonly CommandMetadata SInterStore = new("SINTERSTORE", W, 1, -1);
    public static readonly CommandMetadata SUnionStore = new("SUNIONSTORE", W, 1, -1);
    public static readonly CommandMetadata SDiffStore = new("SDIFFSTORE", W, 1, -1);
    public static readonly CommandMetadata SScan = new("SSCAN", R, 1, 1);

    // Sorted sets
    public static readonly CommandMetadata ZAdd = new("ZADD", W, 1, 1);
    public static readonly CommandMetadata ZIncrBy = new("ZINCRBY", W, 1, 1);
    public static readonly CommandMetadata ZRem = new("ZREM", W, 1, 1);
    public static readonly CommandMetadata ZScore = new("ZSCORE", R, 1, 1);
    public static readonly CommandMetadata ZMScore = new("ZMSCORE", R, 1, 1);
    public static readonly CommandMetadata ZRank = new("ZRANK", R, 1, 1);
    public static readonly CommandMetadata ZRevRank = new("ZREVRANK", R, 1, 1);
    public static readonly CommandMetadata ZCard = new("ZCARD", R, 1, 1);
    public static readonly CommandMetadata ZCount = new("ZCOUNT", R, 1, 1);
    public static readonly CommandMetadata ZRange = new("ZRANGE", R, 1, 1);
    public static readonly CommandMetadata ZRangeStore = new("ZRANGESTORE", W, 1, 2);
    public static readonly CommandMetadata ZRemRangeByRank = new("ZREMRANGEBYRANK", W, 1, 1);
    public static readonly CommandMetadata ZRemRangeByScore = new("ZREMRANGEBYSCORE", W, 1, 1);
    public static readonly CommandMetadata ZRemRangeByLex = new("ZREMRANGEBYLEX", W, 1, 1);
    public static readonly CommandMetadata ZPopMin = new("ZPOPMIN", W, 1, 1);
    public static readonly CommandMetadata ZPopMax = new("ZPOPMAX", W, 1, 1);
    public static readonly CommandMetadata BZPopMin = new("BZPOPMIN", W, 1, -2, blocking: BlockingTimeout.LastArgumentSeconds);
    public static readonly CommandMetadata BZPopMax = new("BZPOPMAX", W, 1, -2, blocking: BlockingTimeout.LastArgumentSeconds);
    public static readonly CommandMetadata BZMPop = new("BZMPOP", W, blocking: BlockingTimeout.FirstArgumentSeconds);
    public static readonly CommandMetadata ZInter = new("ZINTER", R);
    public static readonly CommandMetadata ZUnion = new("ZUNION", R);
    public static readonly CommandMetadata ZDiff = new("ZDIFF", R);
    public static readonly CommandMetadata ZInterStore = new("ZINTERSTORE", W);
    public static readonly CommandMetadata ZUnionStore = new("ZUNIONSTORE", W);
    public static readonly CommandMetadata ZDiffStore = new("ZDIFFSTORE", W);
    public static readonly CommandMetadata ZScan = new("ZSCAN", R, 1, 1);

    // Streams (no typed API yet, but XREAD BLOCK must be routed to a dedicated connection)
    public static readonly CommandMetadata XRead = new("XREAD", R, blocking: BlockingTimeout.BlockOptionMilliseconds);
    public static readonly CommandMetadata XReadGroup = new("XREADGROUP", W, blocking: BlockingTimeout.BlockOptionMilliseconds);

    private static readonly FrozenDictionary<string, CommandMetadata> Known = new[]
    {
        Ping, Echo, Hello, Auth, Select, Client, Info, Quit, Reset, Monitor, Wait, WaitAof,
        Multi, Exec, Discard, Watch, Unwatch,
        Eval, EvalSha, EvalRo, EvalShaRo, Script,
        Subscribe, Unsubscribe, PSubscribe, PUnsubscribe, SSubscribe, SUnsubscribe, Publish,
        Del, Unlink, Exists, Expire, PExpire, ExpireAt, PExpireAt, Ttl, PTtl, Persist, Type, Rename, RenameNx, Copy, Touch, Scan,
        Get, Set, MGet, MSet, MSetNx, GetDel, GetEx, GetRange, SetRange, Append, StrLen, Incr, IncrBy, IncrByFloat, Decr, DecrBy,
        HGet, HMGet, HSet, HSetNx, HDel, HExists, HLen, HStrLen, HGetAll, HKeys, HVals, HIncrBy, HIncrByFloat, HRandField, HScan,
        LPush, RPush, LPushX, RPushX, LPop, RPop, LRange, LLen, LIndex, LSet, LInsert, LRem, LTrim, LPos, LMove,
        BLPop, BRPop, BLMove, BRPopLPush, BLMPop,
        SAdd, SRem, SMembers, SIsMember, SMIsMember, SCard, SPop, SRandMember, SMove,
        SInter, SUnion, SDiff, SInterStore, SUnionStore, SDiffStore, SScan,
        ZAdd, ZIncrBy, ZRem, ZScore, ZMScore, ZRank, ZRevRank, ZCard, ZCount, ZRange, ZRangeStore,
        ZRemRangeByRank, ZRemRangeByScore, ZRemRangeByLex, ZPopMin, ZPopMax, BZPopMin, BZPopMax, BZMPop,
        ZInter, ZUnion, ZDiff, ZInterStore, ZUnionStore, ZDiffStore, ZScan,
        XRead, XReadGroup,
    }.ToFrozenDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns metadata for a command name, creating neutral metadata for unknown commands.</summary>
    public static CommandMetadata Lookup(string name) =>
        Known.TryGetValue(name, out var metadata) ? metadata : new CommandMetadata(name.ToUpperInvariant(), CommandFlags.None);
}
