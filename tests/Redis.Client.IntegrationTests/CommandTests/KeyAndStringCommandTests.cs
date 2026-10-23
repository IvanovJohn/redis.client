using Redis.Client.IntegrationTests.Infrastructure;

namespace Redis.Client.IntegrationTests.CommandTests;

public abstract class KeyAndStringCommandTests(RedisFixture fixture, RedisProtocol protocol) : CommandTestsBase(fixture, protocol)
{
    [Fact]
    public async Task Missing_key_reads_as_null()
    {
        var value = await Client.Strings.GetAsync(Key("missing"));
        Assert.True(value.IsNull);
    }

    [Fact]
    public async Task Number_round_trips()
    {
        await Client.Strings.SetAsync(Key("n"), 42);
        Assert.Equal(42, (long)await Client.Strings.GetAsync(Key("n")));
    }

    [Fact]
    public async Task Set_with_conditions_and_expiry()
    {
        var key = Key("k");
        Assert.True(await Client.Strings.SetAsync(key, "v1"));
        Assert.False(await Client.Strings.SetAsync(key, "v2", when: When.NotExists));
        Assert.Equal("v1", (string?)await Client.Strings.GetAsync(key));
        Assert.False(await Client.Strings.SetAsync(Key("absent"), "v", when: When.Exists));

        Assert.True(await Client.Strings.SetAsync(key, "v3", expiry: TimeSpan.FromSeconds(10)));
        var ttl = await Client.Keys.TimeToLiveAsync(key);
        Assert.Equal(TimeToLiveKind.Expires, ttl.Kind);
        Assert.InRange(ttl.Remaining, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));

        Assert.True(await Client.Strings.SetAsync(key, "v4", keepTtl: true));
        Assert.Equal(TimeToLiveKind.Expires, (await Client.Keys.TimeToLiveAsync(key)).Kind);

        Assert.True(await Client.Strings.SetAsync(key, "v5", DateTimeOffset.UtcNow.AddMinutes(5)));
        Assert.InRange((await Client.Keys.TimeToLiveAsync(key)).Remaining, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));

        Assert.Equal("v5", (string?)await Client.Strings.SetAndGetAsync(key, "v6"));
        Assert.True((await Client.Strings.SetAndGetAsync(Key("fresh"), "x")).IsNull);
    }

    [Fact]
    public async Task Multi_get_returns_null_for_missing_keys()
    {
        await Client.Strings.SetAsync(Key("a"), "1");
        var values = await Client.Strings.GetAsync([Key("a"), Key("b")]);
        Assert.Equal(2, values.Length);
        Assert.Equal("1", (string?)values[0]);
        Assert.True(values[1].IsNull);
    }

    [Fact]
    public async Task Multi_set()
    {
        Assert.True(await Client.Strings.SetAsync([(Key("m1"), "1"), (Key("m2"), "2")]));
        Assert.False(await Client.Strings.SetAsync([(Key("m1"), "x"), (Key("m3"), "3")], When.NotExists));
        Assert.Equal(["1", "2"], (await Client.Strings.GetAsync([Key("m1"), Key("m2")])).Select(v => (string)v!));
        Assert.False(await Client.Keys.ExistsAsync(Key("m3")));
    }

    [Fact]
    public async Task String_operations()
    {
        var key = Key("s");
        Assert.Equal(5, await Client.Strings.AppendAsync(key, "hello"));
        Assert.Equal(11, await Client.Strings.AppendAsync(key, " world"));
        Assert.Equal(11, await Client.Strings.LengthAsync(key));
        Assert.Equal("world", (string?)await Client.Strings.GetRangeAsync(key, 6, -1));
        Assert.Equal(11, await Client.Strings.SetRangeAsync(key, 0, "HELLO"));
        Assert.Equal("HELLO world", (string?)await Client.Strings.GetDeleteAsync(key));
        Assert.False(await Client.Keys.ExistsAsync(key));
    }

    [Fact]
    public async Task Get_and_expire()
    {
        var key = Key("gx");
        await Client.Strings.SetAsync(key, "v");
        Assert.Equal("v", (string?)await Client.Strings.GetAndExpireAsync(key, TimeSpan.FromMilliseconds(30_500)));
        Assert.Equal(TimeToLiveKind.Expires, (await Client.Keys.TimeToLiveAsync(key)).Kind);
        Assert.Equal("v", (string?)await Client.Strings.GetAndExpireAsync(key, (TimeSpan?)null));
        Assert.Equal(TimeToLiveKind.NoExpiry, (await Client.Keys.TimeToLiveAsync(key)).Kind);
        Assert.Equal("v", (string?)await Client.Strings.GetAndExpireAsync(key, DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Equal(TimeToLiveKind.Expires, (await Client.Keys.TimeToLiveAsync(key)).Kind);
    }

    [Fact]
    public async Task Counters()
    {
        var key = Key("c");
        Assert.Equal(1, await Client.Strings.IncrementAsync(key));
        Assert.Equal(11, await Client.Strings.IncrementAsync(key, 10));
        Assert.Equal(10, await Client.Strings.DecrementAsync(key));
        Assert.Equal(7, await Client.Strings.DecrementAsync(key, 3));
        Assert.Equal(8.5, await Client.Strings.IncrementAsync(key, 1.5));
    }

    [Fact]
    public async Task Wrong_type_is_a_server_error()
    {
        await Client.Lists.LeftPushAsync(Key("list"), "x");
        var error = await Assert.ThrowsAsync<RedisServerException>(() => Client.Strings.GetAsync(Key("list")).AsTask());
        Assert.Equal("WRONGTYPE", error.ErrorCode);
    }

    [Fact]
    public async Task Time_to_live_distinguishes_persistent_and_missing_keys()
    {
        await Client.Strings.SetAsync(Key("p"), "v");
        Assert.Equal(TimeToLive.NoExpiry, await Client.Keys.TimeToLiveAsync(Key("p")));
        Assert.Equal(TimeToLive.KeyMissing, await Client.Keys.TimeToLiveAsync(Key("none")));

        Assert.True(await Client.Keys.ExpireAsync(Key("p"), TimeSpan.FromSeconds(100)));
        var ttl = await Client.Keys.TimeToLiveAsync(Key("p"));
        Assert.Equal(TimeToLiveKind.Expires, ttl.Kind);
        Assert.InRange(ttl.Remaining, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(100));

        Assert.True(await Client.Keys.ExpireAsync(Key("p"), TimeSpan.FromMilliseconds(90_500)));
        Assert.True(await Client.Keys.ExpireAtAsync(Key("p"), DateTimeOffset.UtcNow.AddMinutes(10)));
        Assert.True(await Client.Keys.PersistAsync(Key("p")));
        Assert.Equal(TimeToLive.NoExpiry, await Client.Keys.TimeToLiveAsync(Key("p")));
        Assert.False(await Client.Keys.ExpireAsync(Key("none"), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Key_management()
    {
        await Client.Strings.SetAsync(Key("a"), "1");
        await Client.Hashes.SetAsync(Key("h"), "f", "v");
        Assert.Equal(RedisType.String, await Client.Keys.TypeAsync(Key("a")));
        Assert.Equal(RedisType.Hash, await Client.Keys.TypeAsync(Key("h")));
        Assert.Equal(RedisType.None, await Client.Keys.TypeAsync(Key("none")));

        Assert.Equal(2, await Client.Keys.ExistsAsync([Key("a"), Key("h"), Key("none")]));
        Assert.True(await Client.Keys.TouchAsync(Key("a")));
        Assert.Equal(1, await Client.Keys.TouchAsync([Key("a"), Key("none")]));

        Assert.True(await Client.Keys.CopyAsync(Key("a"), Key("copy")));
        Assert.False(await Client.Keys.CopyAsync(Key("a"), Key("copy")));
        Assert.True(await Client.Keys.CopyAsync(Key("a"), Key("copy"), replace: true));

        Assert.True(await Client.Keys.RenameAsync(Key("copy"), Key("renamed")));
        Assert.False(await Client.Keys.RenameAsync(Key("renamed"), Key("a"), When.NotExists));
        await Assert.ThrowsAsync<RedisServerException>(() => Client.Keys.RenameAsync(Key("none"), Key("x")).AsTask());

        Assert.True(await Client.Keys.DeleteAsync(Key("renamed")));
        Assert.False(await Client.Keys.DeleteAsync(Key("renamed")));
        Assert.True(await Client.Keys.UnlinkAsync(Key("h")));
        await Client.Strings.SetAsync(Key("b"), "2");
        Assert.Equal(2, await Client.Keys.DeleteAsync([Key("a"), Key("b"), Key("none")]));
        Assert.Equal(0, await Client.Keys.UnlinkAsync([Key("a")]));
    }

    [Fact]
    public async Task Scan_visits_every_key()
    {
        const int Count = 10_000;
        var scanPrefix = Prefix + "scan:";
        for (var batch = 0; batch < Count; batch += 1000)
        {
            await Client.Strings.SetAsync(Enumerable.Range(batch, 1000).Select(i => ((RedisKey)$"{scanPrefix}{i}", (RedisValue)i)).ToArray());
        }

        var seen = new HashSet<string>();
        await foreach (var key in Client.Keys.ScanAsync(pattern: scanPrefix + "*", pageSize: 500))
        {
            seen.Add(key.ToString());
        }

        Assert.Equal(Count, seen.Count);

        var typed = 0;
        await foreach (var _ in Client.Keys.ScanAsync(pattern: scanPrefix + "*", type: RedisType.Hash))
        {
            typed++;
        }

        Assert.Equal(0, typed);
    }

    [Fact]
    public async Task Scan_stops_on_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await Client.Strings.SetAsync([(Key("x1"), "1"), (Key("x2"), "2")]);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Client.Keys.ScanAsync(pattern: Prefix + "*", cancellationToken: cancellation.Token))
            {
            }
        });
    }
}

[Collection(RedisCollection.Name)]
public sealed class KeyAndStringCommandsResp2(RedisFixture fixture) : KeyAndStringCommandTests(fixture, RedisProtocol.Resp2);

[Collection(RedisCollection.Name)]
public sealed class KeyAndStringCommandsResp3(RedisFixture fixture) : KeyAndStringCommandTests(fixture, RedisProtocol.Resp3);
