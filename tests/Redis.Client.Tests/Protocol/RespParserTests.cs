using System.Buffers;
using System.Globalization;
using System.Text;
using Redis.Client.Protocol;

namespace Redis.Client.Tests.Protocol;

public sealed class RespParserTests
{
    [Theory]
    [InlineData("+OK\r\n", "+OK")]
    [InlineData("-ERR boom\r\n", "-ERR boom")]
    [InlineData(":42\r\n", ":42")]
    [InlineData(":-7\r\n", ":-7")]
    [InlineData("$5\r\nhello\r\n", "$hello")]
    [InlineData("$0\r\n\r\n", "$")]
    [InlineData("$-1\r\n", "nil")]
    [InlineData("*-1\r\n", "nil")]
    [InlineData("*0\r\n", "[]")]
    [InlineData("*2\r\n*1\r\n:1\r\n$1\r\na\r\n", "[[:1],$a]")]
    public void Parses_resp2_types(string input, string expected)
    {
        Assert.Equal(expected, ParseSingle(input));
    }

    [Theory]
    [InlineData("_\r\n", "nil")]
    [InlineData("#t\r\n", "#t")]
    [InlineData("#f\r\n", "#f")]
    [InlineData(",1.5\r\n", ",1.5")]
    [InlineData(",-2\r\n", ",-2")]
    [InlineData(",1e3\r\n", ",1000")]
    [InlineData("(3492890328409238509324850943850943825024385\r\n", "(3492890328409238509324850943850943825024385")]
    [InlineData("!21\r\nSYNTAX invalid syntax\r\n", "!SYNTAX invalid syntax")]
    [InlineData("=15\r\ntxt:Some string\r\n", "=txt:Some string")]
    [InlineData("%1\r\n+field\r\n:42\r\n", "{+field=:42}")]
    [InlineData("~2\r\n+a\r\n+b\r\n", "~[+a,+b]")]
    [InlineData(">3\r\n$7\r\nmessage\r\n$2\r\nch\r\n$1\r\nx\r\n", ">[$message,$ch,$x]")]
    public void Parses_resp3_types(string input, string expected)
    {
        Assert.Equal(expected, ParseSingle(input));
    }

    [Theory]
    [InlineData(",inf\r\n", double.PositiveInfinity)]
    [InlineData(",-inf\r\n", double.NegativeInfinity)]
    public void Parses_special_doubles(string input, double expected)
    {
        var value = SequenceBuilder.Feed(Encoding.ASCII.GetBytes(input), [input.Length], f => f.GetDouble()).Single();
        Assert.Equal(expected, value);
    }

    [Fact]
    public void Parses_nan()
    {
        var value = SequenceBuilder.Feed(",nan\r\n"u8.ToArray(), [6], f => f.GetDouble()).Single();
        Assert.True(double.IsNaN(value));
    }

    [Fact]
    public void Verbatim_string_exposes_format_and_text()
    {
        var (format, text) = SequenceBuilder.Feed("=15\r\ntxt:Some string\r\n"u8.ToArray(), [100], f => (f.GetVerbatimFormat(), f.GetString())).Single();
        Assert.Equal("txt", format);
        Assert.Equal("Some string", text);
    }

    [Fact]
    public void Attribute_before_reply_is_not_a_separate_reply()
    {
        var input = "|1\r\n+key-popularity\r\n%1\r\n$1\r\na\r\n,0.19\r\n*2\r\n:1\r\n:2\r\n:3\r\n"u8.ToArray();
        var frames = SequenceBuilder.Feed(input, [input.Length], f => (Dump: FrameDump.Dump(f), f.HasAttributes));
        Assert.Equal(2, frames.Count);
        Assert.Equal(("[:1,:2]", true), frames[0]);
        Assert.Equal((":3", false), frames[1]);
    }

    [Fact]
    public void Attribute_inside_aggregate_is_skipped()
    {
        var input = "*2\r\n|1\r\n+a\r\n+b\r\n:1\r\n:2\r\n"u8.ToArray();
        Assert.Equal("[:1,:2]", Assert.Single(Parse(input)));
    }

    [Fact]
    public void Reply_arriving_byte_by_byte_is_parsed_once_complete()
    {
        var data = "$5\r\nhello\r\n"u8.ToArray();
        var scanner = new RespScanner(RespLimits.Default);
        for (var length = 1; length < data.Length; length++)
        {
            Assert.False(scanner.TryScan(SequenceBuilder.Create(data[..length]), out _));
        }

        Assert.True(scanner.TryScan(SequenceBuilder.Create(data), out var frameLength));
        Assert.Equal(data.Length, frameLength);
        Assert.Equal("$hello", FrameDump.Dump(RespFrame.Read(SequenceBuilder.Create(data))));
    }

    [Fact]
    public void Reply_split_between_cr_and_lf_is_parsed()
    {
        var data = "*2\r\n$1\r\na\r\n:5\r\n"u8.ToArray();
        for (var split = 1; split < data.Length; split++)
        {
            var frames = SequenceBuilder.Feed(data, [split], FrameDump.Dump);
            Assert.Equal(["[$a,:5]"], frames);
        }
    }

    [Fact]
    public void Several_replies_in_one_read_and_partial_fourth_are_handled()
    {
        var data = "+OK\r\n:1\r\n$1\r\nx\r\n$5\r\nhel"u8.ToArray();
        var scanner = new RespScanner(RespLimits.Default);
        var buffer = SequenceBuilder.Create(data);
        var frames = new List<string>();
        while (scanner.TryScan(buffer, out var length))
        {
            frames.Add(FrameDump.Dump(RespFrame.Read(buffer.Slice(0, length))));
            buffer = buffer.Slice(length);
        }

        Assert.Equal(["+OK", ":1", "$x"], frames);
        Assert.Equal(7, buffer.Length);

        var rest = SequenceBuilder.Create(buffer.ToArray(), "lo\r\n"u8.ToArray());
        Assert.True(scanner.TryScan(rest, out var last));
        Assert.Equal("$hello", FrameDump.Dump(RespFrame.Read(rest.Slice(0, last))));
    }

    [Fact]
    public void Large_aggregate_arriving_in_small_chunks_is_validated_incrementally()
    {
        const int Elements = 20_000;
        var builder = new StringBuilder($"*{Elements}\r\n");
        for (var i = 0; i < Elements; i++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"$6\r\nv{i % 100000:D5}\r\n");
        }

        var data = Encoding.ASCII.GetBytes(builder.ToString());
        var scanner = new RespScanner(RespLimits.Default);
        var received = 0;
        var calls = 0;
        while (true)
        {
            received = Math.Min(data.Length, received + 64);
            calls++;
            if (scanner.TryScan(SequenceBuilder.Create(data[..received]), out var length))
            {
                Assert.Equal(data.Length, length);
                break;
            }

            // Progress is kept between calls: everything but the element in flight is already validated.
            Assert.True(scanner.ValidatedBytes > received - 64);
        }

        Assert.True(calls > 100);
    }

    [Fact]
    public void Oversized_bulk_string_is_rejected_before_payload_arrives()
    {
        var scanner = new RespScanner(new RespLimits(1024, 8));
        var error = Assert.Throws<RedisProtocolException>(() => scanner.TryScan(SequenceBuilder.Create("$1025\r\n"u8.ToArray()), out _));
        Assert.Contains("1025", error.Message);
    }

    [Fact]
    public void Oversized_aggregate_is_rejected()
    {
        var scanner = new RespScanner(new RespLimits(10, 8));
        Assert.Throws<RedisProtocolException>(() => scanner.TryScan(SequenceBuilder.Create("*11\r\n"u8.ToArray()), out _));
    }

    [Fact]
    public void Nesting_deeper_than_limit_is_rejected()
    {
        var scanner = new RespScanner(new RespLimits(1024, 3));
        Assert.True(scanner.TryScan(SequenceBuilder.Create("*1\r\n*1\r\n*1\r\n:1\r\n"u8.ToArray()), out _));
        Assert.Throws<RedisProtocolException>(() => scanner.TryScan(SequenceBuilder.Create("*1\r\n*1\r\n*1\r\n*1\r\n:1\r\n"u8.ToArray()), out _));
    }

    [Theory]
    [InlineData("@foo\r\n")]
    [InlineData(":12a\r\n")]
    [InlineData("$abc\r\n")]
    [InlineData("$-2\r\n")]
    [InlineData("*-5\r\n")]
    [InlineData("$3\r\nabcXY")]
    [InlineData("#x\r\n")]
    [InlineData("_x\r\n")]
    [InlineData(",abc\r\n")]
    [InlineData("=2\r\nab\r\n")]
    [InlineData("=5\r\nabcde\r\n")]
    [InlineData("%-1\r\n")]
    public void Malformed_input_throws_protocol_exception(string input)
    {
        var scanner = new RespScanner(RespLimits.Default);
        Assert.Throws<RedisProtocolException>(() => scanner.TryScan(SequenceBuilder.Create(Encoding.ASCII.GetBytes(input)), out _));
    }

    [Fact]
    public void Line_without_terminator_is_rejected_once_too_long()
    {
        var scanner = new RespScanner(RespLimits.Default);
        Assert.False(scanner.TryScan(SequenceBuilder.Create(":123"u8.ToArray()), out _));
        Assert.Throws<RedisProtocolException>(() => scanner.TryScan(SequenceBuilder.Create(Encoding.ASCII.GetBytes(":" + new string('1', 100))), out _));
    }

    [Fact]
    public void Error_frames_become_server_exceptions()
    {
        var error = SequenceBuilder.Feed("-WRONGTYPE Operation against a key\r\n"u8.ToArray(), [100], f => (f.IsError, Exception: f.ToServerException())).Single();
        Assert.True(error.IsError);
        Assert.Equal("WRONGTYPE", error.Exception.ErrorCode);
        Assert.Equal("WRONGTYPE Operation against a key", error.Exception.Message);

        var bulkError = SequenceBuilder.Feed("!10\r\nNOPERM bad\r\n"u8.ToArray(), [100], f => f.ToServerException()).Single();
        Assert.Equal("NOPERM", bulkError.ErrorCode);
    }

    [Fact]
    public void Scalars_convert_to_values()
    {
        Assert.Equal(42L, (long)Convert(":42\r\n"));
        Assert.Equal("hi", (string?)Convert("$2\r\nhi\r\n"));
        Assert.True(Convert("$-1\r\n").IsNull);
        Assert.Equal(1L, (long)Convert("#t\r\n"));
        Assert.Equal(2.5, (double)Convert(",2.5\r\n"));
    }

    private static RedisValue Convert(string input) =>
        SequenceBuilder.Feed(Encoding.ASCII.GetBytes(input), [input.Length], f => f.ToRedisValue()).Single();

    private static string ParseSingle(string input) => Assert.Single(Parse(Encoding.ASCII.GetBytes(input)));

    private static List<string> Parse(byte[] input) => SequenceBuilder.Feed(input, [input.Length], FrameDump.Dump);
}
