using System.Buffers;
using System.Text;
using BenchmarkDotNet.Attributes;
using Redis.Client.Protocol;

namespace Redis.Client.Benchmarks;

/// <summary>Hot-path cost of serializing commands and parsing replies (no network).</summary>
[MemoryDiagnoser]
public class ProtocolBenchmarks
{
    private readonly ArrayBufferWriter<byte> _buffer = new(4096);
    private readonly RespScanner _scanner = new(RespLimits.Default);
    private readonly RedisCommand _set = new(Commands.Set, "user:12345:profile", "some-value-of-moderate-length-0123456789");
    private ReadOnlySequence<byte> _bulkReply;
    private ReadOnlySequence<byte> _integerReply;
    private ReadOnlySequence<byte> _arrayReply;

    [GlobalSetup]
    public void Setup()
    {
        _bulkReply = new ReadOnlySequence<byte>("$40\r\nsome-value-of-moderate-length-0123456789\r\n"u8.ToArray());
        _integerReply = new ReadOnlySequence<byte>(":1234567\r\n"u8.ToArray());
        var array = new StringBuilder("*100\r\n");
        for (var i = 0; i < 100; i++)
        {
            array.Append("$8\r\nvalue").Append(i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)).Append("\r\n");
        }

        _arrayReply = new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(array.ToString()));
    }

    [Benchmark]
    public int WriteSetCommand()
    {
        _buffer.ResetWrittenCount();
        RespWriter.WriteCommand(_buffer, _set);
        return _buffer.WrittenCount;
    }

    [Benchmark]
    public long ScanIntegerReply()
    {
        _scanner.TryScan(_integerReply, out var length);
        return RespFrame.Read(_integerReply.Slice(0, length)).GetInt64();
    }

    /// <summary>Validation and navigation of a bulk reply, without copying the payload.</summary>
    [Benchmark]
    public long ScanBulkReply()
    {
        _scanner.TryScan(_bulkReply, out var length);
        return RespFrame.Read(_bulkReply.Slice(0, length)).Payload.Length;
    }

    /// <summary>Full GET path: validate and copy the value out (one allocation for the value itself).</summary>
    [Benchmark]
    public RedisValue ConvertBulkReply()
    {
        _scanner.TryScan(_bulkReply, out var length);
        return RespFrame.Read(_bulkReply.Slice(0, length)).ToRedisValue();
    }

    [Benchmark]
    public int ScanArrayOf100()
    {
        _scanner.TryScan(_arrayReply, out var length);
        var count = 0;
        foreach (var child in RespFrame.Read(_arrayReply.Slice(0, length)).GetChildren())
        {
            count += (int)child.Payload.Length;
        }

        return count;
    }
}
