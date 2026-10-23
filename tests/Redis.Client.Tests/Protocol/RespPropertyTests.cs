using System.Globalization;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Redis.Client.Tests.Protocol;

/// <summary>Model of a RESP frame used to generate wire data together with the expected parse result.</summary>
public sealed record TestFrame(char Type, string Text, TestFrame[] Children, bool WithAttribute)
{
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    public void Encode(List<byte> output)
    {
        if (WithAttribute)
        {
            output.AddRange("|1\r\n+meta\r\n:1\r\n"u8.ToArray());
        }

        switch (Type)
        {
            case '$' or '!':
                var bytes = Encoding.UTF8.GetBytes(Text);
                output.AddRange(Encoding.ASCII.GetBytes($"{Type}{bytes.Length}\r\n"));
                output.AddRange(bytes);
                output.AddRange(Crlf);
                break;
            case '=':
                var verbatim = Encoding.UTF8.GetBytes("txt:" + Text);
                output.AddRange(Encoding.ASCII.GetBytes($"={verbatim.Length}\r\n"));
                output.AddRange(verbatim);
                output.AddRange(Crlf);
                break;
            case 'N':
                // Text holds the null encoding: "_", "$-1" or "*-1".
                output.AddRange(Encoding.ASCII.GetBytes(Text + "\r\n"));
                break;
            case '*' or '~' or '>':
                output.AddRange(Encoding.ASCII.GetBytes($"{Type}{Children.Length}\r\n"));
                foreach (var child in Children)
                {
                    child.Encode(output);
                }

                break;
            case '%':
                output.AddRange(Encoding.ASCII.GetBytes($"%{Children.Length / 2}\r\n"));
                foreach (var child in Children)
                {
                    child.Encode(output);
                }

                break;
            default:
                output.AddRange(Encoding.UTF8.GetBytes($"{Type}{Text}\r\n"));
                break;
        }
    }

    /// <summary>The string <see cref="FrameDump"/> must produce for this frame.</summary>
    public string Expected() => Type switch
    {
        'N' => "nil",
        '*' => "[" + string.Join(",", Children.Select(c => c.Expected())) + "]",
        '~' => "~[" + string.Join(",", Children.Select(c => c.Expected())) + "]",
        '>' => ">[" + string.Join(",", Children.Select(c => c.Expected())) + "]",
        '%' => "{" + string.Join(",", Children.Chunk(2).Select(p => p[0].Expected() + "=" + p[1].Expected())) + "}",
        ',' => "," + double.Parse(Text, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture),
        '=' => "=txt:" + Text,
        _ => Type + Text,
    };

    public override string ToString() => Expected();
}

public sealed record FrameCase(TestFrame[] Frames, int[] Chunks)
{
    public byte[] Encode()
    {
        var output = new List<byte>();
        foreach (var frame in Frames)
        {
            frame.Encode(output);
        }

        return [.. output];
    }

    public override string ToString() => $"{string.Join(" ", Frames.Select(f => f.Expected()))} chunks=[{string.Join(",", Chunks)}]";
}

public sealed record FuzzCase(byte[] Data, int[] Chunks)
{
    public override string ToString() => $"\"{Encoding.ASCII.GetString(Data).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\" chunks=[{string.Join(",", Chunks)}]";
}

public static class RespArbitraries
{
    private static readonly char[] SafeChars = "abcXYZ019 :_-".ToCharArray();
    private static readonly char[] AnyChars = "ab\r\n\0 ключ$*:".ToCharArray();

    public static Arbitrary<FrameCase> FrameCases() =>
        (from frames in Gen.ArrayOf(Frame(3), 3).Where(a => a.Length > 0)
         from chunks in Chunks()
         select new FrameCase(frames, chunks)).ToArbitrary();

    public static Arbitrary<FuzzCase> FuzzCases()
    {
        // Random soup biased towards RESP syntax, plus mutations of valid frames.
        var alphabet = "+-:$*_#,(!=%~>|0123456789\r\n-tfab".Select(c => (byte)c).ToArray();
        var soup = Gen.ArrayOf(Gen.Elements(alphabet));
        var mutated =
            from frame in Frame(3)
            from position in Gen.Choose(0, 1000)
            from replacement in Gen.Elements(alphabet)
            from truncate in Gen.Elements(true, false)
            select Mutate(frame, position, replacement, truncate);
        return (from data in Gen.OneOf(soup, mutated)
                from chunks in Chunks()
                select new FuzzCase(data, chunks)).ToArbitrary();
    }

    private static byte[] Mutate(TestFrame frame, int position, byte replacement, bool truncate)
    {
        var output = new List<byte>();
        frame.Encode(output);
        var index = position % output.Count;
        if (truncate)
        {
            output.RemoveRange(index, output.Count - index);
        }
        else
        {
            output[index] = replacement;
        }

        return [.. output];
    }

    private static Gen<int[]> Chunks() => Gen.ArrayOf(Gen.Choose(1, 40), 20);

    private static Gen<TestFrame> Frame(int depth)
    {
        var text = Gen.ArrayOf(Gen.Elements(SafeChars), 12).Select(c => new string(c));
        var binaryText = Gen.ArrayOf(Gen.Elements(AnyChars), 12).Select(c => new string(c));
        var attribute = Gen.Frequency((1, Gen.Constant(true)), (6, Gen.Constant(false)));

        Gen<TestFrame> Scalar(char type, Gen<string> content) =>
            from t in content
            from a in attribute
            select new TestFrame(type, t, [], a);

        var scalars = Gen.OneOf(
            Scalar('+', text),
            Scalar('-', text.Select(t => "ERR " + t)),
            Scalar(':', Gen.Choose(int.MinValue, int.MaxValue).Select(i => i.ToString(CultureInfo.InvariantCulture))),
            Scalar('$', binaryText),
            Scalar('!', binaryText.Select(t => "ERR " + t)),
            Scalar('=', binaryText),
            Scalar(',', Gen.Choose(-100000, 100000).Select(i => (i / 7.0).ToString("R", CultureInfo.InvariantCulture))),
            Scalar('#', Gen.Elements("t", "f")),
            Scalar('(', Gen.Choose(0, int.MaxValue).Select(i => "1234567890123456789012345" + i.ToString(CultureInfo.InvariantCulture))),
            Scalar('N', Gen.Elements("_", "$-1", "*-1")));

        if (depth == 0)
        {
            return scalars;
        }

        var child = Frame(depth - 1);
        Gen<TestFrame> Aggregate(char type, Gen<TestFrame[]> children) =>
            from c in children
            from a in attribute
            select new TestFrame(type, string.Empty, c, a);

        return Gen.Frequency(
            (4, scalars),
            (1, Aggregate('*', Gen.ArrayOf(child, 4))),
            (1, Aggregate('~', Gen.ArrayOf(child, 4))),
            (1, Aggregate('>', Gen.ArrayOf(child, 4))),
            (1, Aggregate('%', Gen.ArrayOf(child, 3).SelectMany(keys => Gen.ArrayOf(child, keys.Length).Select(values => keys.Zip(values).SelectMany(p => new[] { p.First, p.Second }).ToArray())))));
    }
}

public sealed class RespPropertyTests
{
    [Property(Arbitrary = [typeof(RespArbitraries)], MaxTest = 500)]
    public void Generated_frames_round_trip_with_any_chunking(FrameCase frameCase)
    {
        var data = frameCase.Encode();
        var parsed = SequenceBuilder.Feed(data, frameCase.Chunks, FrameDump.Dump);
        Assert.Equal(frameCase.Frames.Select(f => f.Expected()), parsed);
    }

    [Property(Arbitrary = [typeof(RespArbitraries)], MaxTest = 2000)]
    public void Random_input_never_fails_with_anything_but_protocol_exception(FuzzCase fuzzCase)
    {
        try
        {
            // Frames that do validate must also be fully navigable.
            SequenceBuilder.Feed(fuzzCase.Data, fuzzCase.Chunks, FrameDump.Dump);
        }
        catch (RedisProtocolException)
        {
        }
    }
}
