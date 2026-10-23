using Redis.Client.IntegrationTests.Infrastructure;

namespace Redis.Client.IntegrationTests;

[Collection(RedisCollection.Name)]
public sealed class ScriptTests(RedisFixture fixture)
{
    private static readonly LuaScript IncrementBy = new("return redis.call('INCRBY', KEYS[1], ARGV[1])");

    [Fact]
    public async Task First_call_falls_back_to_eval_and_later_calls_use_evalsha()
    {
        var key = RedisFixture.Prefix();
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        await fixture.Server.CliAsync("SCRIPT", "FLUSH");
        var before = await CallsAsync();

        Assert.Equal(5, (long)await client.ScriptEvaluateAsync(IncrementBy, [key], [5]));
        var afterFirst = await CallsAsync();
        Assert.Equal(1, afterFirst.Eval - before.Eval);
        Assert.Equal(1, afterFirst.EvalSha - before.EvalSha);

        Assert.Equal(8, (long)await client.ScriptEvaluateAsync(IncrementBy, [key], [3]));
        var afterSecond = await CallsAsync();
        Assert.Equal(afterFirst.Eval, afterSecond.Eval);
        Assert.Equal(1, afterSecond.EvalSha - afterFirst.EvalSha);
    }

    [Fact]
    public async Task Script_load_returns_the_precomputed_digest()
    {
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        Assert.Equal(IncrementBy.Sha1, await client.ScriptLoadAsync(IncrementBy));
    }

    [Fact]
    public async Task Error_inside_script_is_a_server_error()
    {
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        var error = await Assert.ThrowsAsync<RedisServerException>(() =>
            client.ScriptEvaluateAsync(new LuaScript("return redis.error_reply('boom')")).AsTask());
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_results_are_converted()
    {
        await using var client = new RedisClient(fixture.Server.CreateOptions());
        var result = await client.ScriptEvaluateAsync(new LuaScript("return {1, 'two', {3}}"));
        Assert.Equal(RedisResultType.Array, result.Type);
        Assert.Equal(1, (long)result[0]);
        Assert.Equal("two", (string?)result[1]);
        Assert.Equal(3, (long)result[2][0]);
    }

    [Fact]
    public async Task Read_only_script_cannot_write_on_redis_7()
    {
        if (RedisServer.ImageVersion < new Version(7, 0))
        {
            return; // EVAL_RO does not exist before Redis 7.0.
        }

        await using var client = new RedisClient(fixture.Server.CreateOptions());
        var key = RedisFixture.Prefix();
        await client.Strings.SetAsync(key, "v");
        Assert.Equal("v", (string?)await client.ScriptEvaluateAsync(new LuaScript("return redis.call('GET', KEYS[1])"), [key], readOnly: true));
        await Assert.ThrowsAsync<RedisServerException>(() =>
            client.ScriptEvaluateAsync(new LuaScript("return redis.call('SET', KEYS[1], 'x')"), [key], readOnly: true).AsTask());
    }

    private async Task<(long Eval, long EvalSha)> CallsAsync()
    {
        var stats = await fixture.Server.CliAsync("INFO", "commandstats");
        return (Calls(stats, "cmdstat_eval:"), Calls(stats, "cmdstat_evalsha:"));

        static long Calls(string stats, string prefix)
        {
            var line = stats.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
            if (line is null)
            {
                return 0;
            }

            var calls = line[(line.IndexOf("calls=", StringComparison.Ordinal) + 6)..];
            return long.Parse(calls[..calls.IndexOf(',', StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
