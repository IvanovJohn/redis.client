using System.Buffers;
using Redis.Client.Protocol;

namespace Redis.Client.Tests.Protocol;

/// <summary>Builds multi-segment sequences and simulates data arriving from the network in pieces.</summary>
internal static class SequenceBuilder
{
    public static ReadOnlySequence<byte> Create(params byte[][] segments)
    {
        if (segments.Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        var first = new Segment(segments[0], 0);
        var last = first;
        foreach (var segment in segments.Skip(1))
        {
            last = last.Append(segment);
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    /// <summary>Splits <paramref name="data"/> into segments of the given sizes (the rest goes into the last one).</summary>
    public static ReadOnlySequence<byte> Split(byte[] data, IEnumerable<int> sizes)
    {
        var parts = new List<byte[]>();
        var offset = 0;
        foreach (var size in sizes)
        {
            if (offset >= data.Length)
            {
                break;
            }

            var take = Math.Clamp(size, 1, data.Length - offset);
            parts.Add(data[offset..(offset + take)]);
            offset += take;
        }

        if (offset < data.Length)
        {
            parts.Add(data[offset..]);
        }

        return Create([.. parts]);
    }

    /// <summary>
    /// Feeds <paramref name="data"/> to one scanner chunk by chunk (each call sees all data received so far, as a
    /// pipe would present it) and returns every frame parsed, converted by <paramref name="convert"/>.
    /// </summary>
    public static List<T> Feed<T>(byte[] data, IEnumerable<int> chunkSizes, Func<RespFrame, T> convert, RespLimits? limits = null)
    {
        var scanner = new RespScanner(limits ?? RespLimits.Default);
        var received = new List<byte[]>();
        var results = new List<T>();
        var consumed = 0L;
        var offset = 0;
        using var sizes = chunkSizes.GetEnumerator();
        while (offset < data.Length)
        {
            var size = sizes.MoveNext() ? Math.Max(1, sizes.Current) : data.Length - offset;
            size = Math.Min(size, data.Length - offset);
            received.Add(data[offset..(offset + size)]);
            offset += size;

            var buffer = Create([.. received]).Slice(consumed);
            while (scanner.TryScan(buffer, out var length))
            {
                results.Add(convert(RespFrame.Read(buffer.Slice(0, length))));
                consumed += length;
                buffer = buffer.Slice(length);
            }
        }

        return results;
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
