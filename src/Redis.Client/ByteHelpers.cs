using System.Buffers;

namespace Redis.Client;

/// <summary>Helpers that give temporary access to the wire bytes of keys and values.</summary>
internal static class ByteHelpers
{
    private const int StackLimit = 256;

    public delegate TResult SpanFunc<TResult>(ReadOnlySpan<byte> bytes);

    public delegate TResult SpanFunc2<TResult>(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b);

    public static TResult WithBytes<TResult>(RedisKey key, SpanFunc<TResult> func)
    {
        var length = key.GetByteCount();
        byte[]? rented = null;
        var buffer = length <= StackLimit ? stackalloc byte[StackLimit] : (rented = ArrayPool<byte>.Shared.Rent(length));
        try
        {
            var written = key.CopyTo(buffer);
            return func(buffer[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public static TResult WithBytes<TResult>(RedisKey first, RedisKey second, SpanFunc2<TResult> func)
    {
        var a = (byte[])first!;
        var b = (byte[])second!;
        return func(a, b);
    }

    public static TResult WithBytes<TResult>(RedisValue value, SpanFunc<TResult> func)
    {
        if (value.TryGetSpan(out var span))
        {
            return func(span);
        }

        var length = value.GetByteCount();
        byte[]? rented = null;
        var buffer = length <= StackLimit ? stackalloc byte[StackLimit] : (rented = ArrayPool<byte>.Shared.Rent(length));
        try
        {
            var written = value.CopyTo(buffer);
            return func(buffer[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    public static int Hash(ReadOnlySpan<byte> bytes)
    {
        var hash = default(HashCode);
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }
}
