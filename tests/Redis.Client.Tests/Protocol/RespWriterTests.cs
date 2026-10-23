using System.Buffers;
using System.Text;
using Redis.Client.Protocol;

namespace Redis.Client.Tests.Protocol;

public sealed class RespWriterTests
{
    [Fact]
    public void Set_command_is_written_as_array_of_bulk_strings()
    {
        var bytes = Write(new RedisCommand(Commands.Set, "key", "value"));
        Assert.Equal("*3\r\n$3\r\nSET\r\n$3\r\nkey\r\n$5\r\nvalue\r\n", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Binary_arguments_are_written_verbatim()
    {
        byte[] value = [0x00, 0x0D, 0x0A, 0xFF];
        var bytes = Write(new RedisCommand(Commands.Set, "k", value));
        byte[] expected = [.. "*3\r\n$3\r\nSET\r\n$1\r\nk\r\n$4\r\n"u8, .. value, .. "\r\n"u8];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Utf8_length_is_in_bytes_not_chars()
    {
        var bytes = Write(new RedisCommand(Commands.Get, "ключ"));
        byte[] expected = [.. "*2\r\n$3\r\nGET\r\n$8\r\n"u8, .. Encoding.UTF8.GetBytes("ключ"), .. "\r\n"u8];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Empty_argument_is_a_zero_length_bulk_string()
    {
        var bytes = Write(new RedisCommand(Commands.Set, "k", ""));
        Assert.Equal("*3\r\n$3\r\nSET\r\n$1\r\nk\r\n$0\r\n\r\n", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Numbers_are_written_in_invariant_format()
    {
        var bytes = Write(new RedisCommand(Commands.IncrByFloat, "k", 1.5, -3L, double.PositiveInfinity));
        Assert.Equal("*5\r\n$11\r\nINCRBYFLOAT\r\n$1\r\nk\r\n$3\r\n1.5\r\n$2\r\n-3\r\n$4\r\n+inf\r\n", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Large_string_argument_is_written_completely()
    {
        var text = string.Concat(Enumerable.Repeat("ключ-", 10_000));
        var utf8 = Encoding.UTF8.GetBytes(text);
        var bytes = Write(new RedisCommand(Commands.Set, "k", text));
        byte[] expected = [.. "*3\r\n$3\r\nSET\r\n$1\r\nk\r\n"u8, .. Encoding.ASCII.GetBytes($"${utf8.Length}\r\n"), .. utf8, .. "\r\n"u8];
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Null_argument_is_rejected_when_command_is_built()
    {
        Assert.Throws<ArgumentException>(() => new RedisCommand(Commands.Set, "k", RedisValue.Null));
    }

    [Theory]
    [InlineData("BLPOP", new[] { "q", "10" }, 10_000)]
    [InlineData("BLPOP", new[] { "q", "0.5" }, 500)]
    [InlineData("BLPOP", new[] { "q", "0" }, -1)]
    [InlineData("BLMPOP", new[] { "2", "1", "q", "LEFT" }, 2_000)]
    [InlineData("WAIT", new[] { "1", "250" }, 250)]
    [InlineData("XREAD", new[] { "BLOCK", "100", "STREAMS", "s", "$" }, 100)]
    public void Blocking_timeout_is_extracted(string name, string[] args, int expectedMs)
    {
        var command = new RedisCommand(Commands.Lookup(name), [.. args.Select(a => (RedisValue)a)]);
        Assert.True(command.IsBlocking);
        var expected = expectedMs < 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(expectedMs);
        Assert.Equal(expected, command.GetBlockingTimeout());
    }

    [Fact]
    public void Xread_without_block_is_not_blocking()
    {
        var command = new RedisCommand(Commands.Lookup("xread"), "STREAMS", "s", "0");
        Assert.False(command.IsBlocking);
        Assert.False(new RedisCommand(Commands.Get, "k").IsBlocking);
    }

    [Fact]
    public void Unknown_command_gets_neutral_metadata()
    {
        var metadata = Commands.Lookup("my.module.cmd");
        Assert.Equal("MY.MODULE.CMD", metadata.Name);
        Assert.False(metadata.IsBlocking);
        Assert.False(metadata.ChangesConnectionState);
        Assert.True(Commands.Lookup("select").ChangesConnectionState);
    }

    private static byte[] Write(RedisCommand command)
    {
        var buffer = new ArrayBufferWriter<byte>();
        RespWriter.WriteCommand(buffer, command);
        return buffer.WrittenSpan.ToArray();
    }
}
