using System.Text;

namespace Redis.Client.Tests;

public sealed class RedisValueTests
{
    [Fact]
    public void Null_and_empty_are_different()
    {
        Assert.True(RedisValue.Null.IsNull);
        Assert.True(RedisValue.Null.IsNullOrEmpty);
        Assert.False(RedisValue.EmptyString.IsNull);
        Assert.True(RedisValue.EmptyString.IsNullOrEmpty);
        Assert.NotEqual(RedisValue.Null, RedisValue.EmptyString);
        Assert.True(((RedisValue)(string?)null).IsNull);
        Assert.True(((RedisValue)(byte[]?)null).IsNull);
    }

    [Fact]
    public void String_is_encoded_as_utf8()
    {
        RedisValue value = "ключ";
        Assert.Equal(Encoding.UTF8.GetBytes("ключ"), (byte[]?)value);
        Assert.Equal(8, value.GetByteCount());
        Assert.Equal("ключ", (string?)value);
    }

    [Fact]
    public void Binary_value_round_trips_without_changes()
    {
        byte[] bytes = [0x00, 0x0D, 0x0A, 0xFF];
        RedisValue value = bytes;
        Assert.Equal(bytes, (byte[]?)value);
        Assert.Equal(4, value.GetByteCount());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42L)]
    [InlineData(-7L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Int64_round_trips_through_bytes(long number)
    {
        RedisValue value = number;
        Assert.Equal(number, (long)value);
        RedisValue fromText = (string?)value;
        Assert.Equal(number, (long)fromText);
        Assert.Equal(number.ToString(System.Globalization.CultureInfo.InvariantCulture), (string?)value);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.1)]
    [InlineData(double.MaxValue)]
    [InlineData(double.Epsilon)]
    [InlineData(3e20)]
    public void Double_round_trips_through_bytes(double number)
    {
        RedisValue value = number;
        RedisValue fromText = (string?)value;
        Assert.Equal(number, (double)fromText);
    }

    [Theory]
    [InlineData("inf", double.PositiveInfinity)]
    [InlineData("+inf", double.PositiveInfinity)]
    [InlineData("-inf", double.NegativeInfinity)]
    [InlineData("INF", double.PositiveInfinity)]
    public void Special_doubles_are_parsed(string text, double expected)
    {
        Assert.Equal(expected, (double)(RedisValue)text);
    }

    [Fact]
    public void Infinities_are_sent_in_redis_format()
    {
        Assert.Equal("+inf", (string?)(RedisValue)double.PositiveInfinity);
        Assert.Equal("-inf", (string?)(RedisValue)double.NegativeInfinity);
        Assert.True(double.IsNaN((double)(RedisValue)"nan"));
    }

    [Fact]
    public void Non_numeric_value_cannot_be_cast_to_number()
    {
        Assert.Throws<InvalidCastException>(() => (long)(RedisValue)"abc");
        Assert.Throws<InvalidCastException>(() => (double)(RedisValue)"abc");
        Assert.Throws<InvalidCastException>(() => (long)RedisValue.Null);
        Assert.Throws<InvalidCastException>(() => (long)(RedisValue)"1.5");
        Assert.Throws<InvalidCastException>(() => (int)(RedisValue)long.MaxValue);
    }

    [Fact]
    public void Nullable_casts_map_nil_to_null()
    {
        Assert.Null((long?)RedisValue.Null);
        Assert.Null((double?)RedisValue.Null);
        Assert.Null((string?)RedisValue.Null);
        Assert.Null((byte[]?)RedisValue.Null);
        Assert.Equal(5, (long?)(RedisValue)"5");
    }

    [Fact]
    public void Integral_double_converts_to_long()
    {
        Assert.Equal(3L, (long)(RedisValue)3.0);
        Assert.False(((RedisValue)3.5).TryParse(out long _));
    }

    [Fact]
    public void Equality_is_by_bytes_across_representations()
    {
        RedisValue number = 42;
        RedisValue text = "42";
        RedisValue bytes = "42"u8.ToArray();
        RedisValue memory = new ReadOnlyMemory<byte>("42"u8.ToArray());
        Assert.Equal(number, text);
        Assert.Equal(text, bytes);
        Assert.Equal(bytes, memory);
        Assert.Equal(number.GetHashCode(), text.GetHashCode());
        Assert.Equal(text.GetHashCode(), bytes.GetHashCode());
        Assert.NotEqual((RedisValue)"a", (RedisValue)"b");
        Assert.True(number == text);
        Assert.False(number != text);
    }

    [Fact]
    public void Large_values_hash_and_compare()
    {
        var big = new string('x', 10_000);
        RedisValue a = big;
        RedisValue b = Encoding.UTF8.GetBytes(big);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ToString_shows_nil_for_null()
    {
        Assert.Equal("(nil)", RedisValue.Null.ToString());
        Assert.Equal("v", ((RedisValue)"v").ToString());
    }
}
