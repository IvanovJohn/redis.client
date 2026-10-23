using System.Globalization;
using System.Text;
using Redis.Client.Protocol;

namespace Redis.Client.Tests.Protocol;

/// <summary>Renders a frame as a compact, comparable string, e.g. <c>[:1,$a]</c> or <c>{+field=:42}</c>.</summary>
internal static class FrameDump
{
    public static string Dump(RespFrame frame)
    {
        var builder = new StringBuilder();
        Append(builder, frame);
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, RespFrame frame)
    {
        switch (frame.Type)
        {
            case RespType.Null:
                builder.Append("nil");
                return;
            case RespType.Array:
            case RespType.Set:
            case RespType.Push:
            {
                builder.Append(frame.Type switch { RespType.Array => "[", RespType.Set => "~[", _ => ">[" });
                var first = true;
                foreach (var child in frame.GetChildren())
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    Append(builder, child);
                }

                builder.Append(']');
                return;
            }

            case RespType.Map:
            {
                builder.Append('{');
                var i = 0;
                foreach (var child in frame.GetChildren())
                {
                    builder.Append(i == 0 ? string.Empty : i % 2 == 1 ? "=" : ",");
                    Append(builder, child);
                    i++;
                }

                builder.Append('}');
                return;
            }

            case RespType.Double:
                builder.Append(',').Append(frame.GetDouble().ToString("R", CultureInfo.InvariantCulture));
                return;
            case RespType.Boolean:
                builder.Append('#').Append(frame.GetBoolean() ? 't' : 'f');
                return;
            case RespType.VerbatimString:
                builder.Append('=').Append(frame.GetVerbatimFormat()).Append(':').Append(frame.GetString());
                return;
            default:
                builder.Append((char)frame.Type).Append(frame.GetString());
                return;
        }
    }
}
