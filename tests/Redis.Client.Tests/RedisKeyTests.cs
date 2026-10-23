using System.Text;

namespace Redis.Client.Tests;

public sealed class RedisKeyTests
{
    [Fact]
    public void Default_key_is_null()
    {
        Assert.True(default(RedisKey).IsNull);
        Assert.True(((RedisKey)(string?)null).IsNull);
        Assert.True(((RedisKey)(byte[]?)null).IsNull);
        Assert.False(((RedisKey)"").IsNull);
    }

    [Fact]
    public void Non_ascii_key_is_encoded_as_utf8()
    {
        RedisKey key = "ключ";
        Assert.Equal(Encoding.UTF8.GetBytes("ключ"), (byte[]?)key);
        Assert.Equal(8, key.GetByteCount());
        Assert.Equal("ключ", key.ToString());
    }

    [Fact]
    public void Binary_key_is_kept_as_is()
    {
        byte[] bytes = [0x00, 0xFF, 0x0D, 0x0A];
        RedisKey key = bytes;
        Assert.Equal(bytes, (byte[]?)key);
    }

    [Fact]
    public void Keys_are_equal_by_bytes()
    {
        RedisKey text = "user:1";
        RedisKey bytes = "user:1"u8.ToArray();
        RedisKey memory = new ReadOnlyMemory<byte>("user:1"u8.ToArray());
        Assert.Equal(text, bytes);
        Assert.Equal(bytes, memory);
        Assert.Equal(text.GetHashCode(), bytes.GetHashCode());
        Assert.NotEqual(text, (RedisKey)"user:2");
        Assert.Equal(default, default(RedisKey));
        Assert.NotEqual(default, (RedisKey)"");
    }

    [Fact]
    public void Key_converts_to_value()
    {
        RedisValue value = (RedisKey)"k";
        Assert.Equal("k", (string?)value);
        Assert.True(((RedisValue)default(RedisKey)).IsNull);
    }
}
