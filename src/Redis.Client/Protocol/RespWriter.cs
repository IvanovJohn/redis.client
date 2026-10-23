using System.Buffers;
using System.Buffers.Text;

namespace Redis.Client.Protocol;

/// <summary>Serializes commands as RESP arrays of bulk strings.</summary>
internal static class RespWriter
{
    private const int MaxHeaderLength = 1 + 20 + 2; // prefix + long + CRLF

    public static void WriteCommand(IBufferWriter<byte> writer, RedisCommand command)
    {
        var args = command.Arguments;
        WriteHeader(writer, (byte)'*', args.Length + 1);
        WriteBulk(writer, command.Metadata.NameBytes);
        foreach (var arg in args)
        {
            WriteBulk(writer, arg);
        }
    }

    private static void WriteBulk(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        WriteHeader(writer, (byte)'$', value.Length);
        writer.Write(value);
        WriteCrlf(writer);
    }

    private static void WriteBulk(IBufferWriter<byte> writer, RedisValue value)
    {
        var length = value.GetByteCount();
        WriteHeader(writer, (byte)'$', length);
        value.WriteTo(writer, length);
        WriteCrlf(writer);
    }

    private static void WriteHeader(IBufferWriter<byte> writer, byte prefix, long length)
    {
        var span = writer.GetSpan(MaxHeaderLength);
        span[0] = prefix;
        Utf8Formatter.TryFormat(length, span[1..], out var written);
        span[1 + written] = (byte)'\r';
        span[2 + written] = (byte)'\n';
        writer.Advance(written + 3);
    }

    private static void WriteCrlf(IBufferWriter<byte> writer)
    {
        var span = writer.GetSpan(2);
        span[0] = (byte)'\r';
        span[1] = (byte)'\n';
        writer.Advance(2);
    }
}
