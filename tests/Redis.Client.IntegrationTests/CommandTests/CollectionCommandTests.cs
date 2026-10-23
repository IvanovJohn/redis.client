using Redis.Client.IntegrationTests.Infrastructure;

namespace Redis.Client.IntegrationTests.CommandTests;

public abstract class CollectionCommandTests(RedisFixture fixture, RedisProtocol protocol) : CommandTestsBase(fixture, protocol)
{
    [Fact]
    public async Task Hash_operations()
    {
        var key = Key("h");
        Assert.Equal(2, await Client.Hashes.SetAsync(key, [("a", 1), ("b", 2)]));
        Assert.Equal(0, await Client.Hashes.SetAsync(key, [("a", 10)]));
        var all = await Client.Hashes.GetAllAsync(key);
        Assert.Equal(new Dictionary<string, string> { ["a"] = "10", ["b"] = "2" }, all.ToDictionary(p => (string)p.Key!, p => (string)p.Value!));

        Assert.Equal("10", (string?)await Client.Hashes.GetAsync(key, "a"));
        Assert.True((await Client.Hashes.GetAsync(key, "zzz")).IsNull);
        Assert.Equal(["10", "(nil)"], await Strings(Client.Hashes.GetAsync(key, ["a", "zzz"])));
        Assert.True(await Client.Hashes.SetAsync(key, "c", "3"));
        Assert.False(await Client.Hashes.SetAsync(key, "c", "4", When.NotExists));
        Assert.True(await Client.Hashes.ExistsAsync(key, "c"));
        Assert.Equal(3, await Client.Hashes.LengthAsync(key));
        Assert.Equal(2, await Client.Hashes.StringLengthAsync(key, "a"));
        Assert.Equal(["a", "b", "c"], (await Client.Hashes.KeysAsync(key)).Select(v => (string)v!).Order());
        Assert.Equal(3, (await Client.Hashes.ValuesAsync(key)).Length);
        Assert.Equal(15, await Client.Hashes.IncrementAsync(key, "a", 5));
        Assert.Equal(2.5, await Client.Hashes.IncrementAsync(key, "b", 0.5));
        Assert.False((await Client.Hashes.RandomFieldAsync(key)).IsNull);
        Assert.Equal(2, (await Client.Hashes.RandomFieldsAsync(key, 2)).Length);
        var random = await Client.Hashes.RandomFieldsWithValuesAsync(key, 3);
        Assert.Equal(3, random.Length);
        Assert.Contains(random, p => (string?)p.Key == "a" && (string?)p.Value == "15");
        Assert.True(await Client.Hashes.DeleteAsync(key, "c"));
        Assert.Equal(1, await Client.Hashes.DeleteAsync(key, ["b", "zzz"]));
        Assert.Empty(await Client.Hashes.GetAllAsync(Key("none")));
    }

    [Fact]
    public async Task Hash_scan()
    {
        var key = Key("hs");
        await Client.Hashes.SetAsync(key, Enumerable.Range(0, 300).Select(i => ((RedisValue)$"f{i}", (RedisValue)i)).ToArray());
        var fields = new Dictionary<string, long>();
        await foreach (var (field, value) in Client.Hashes.ScanAsync(key, pageSize: 50))
        {
            fields[(string)field!] = (long)value;
        }

        Assert.Equal(300, fields.Count);
        Assert.Equal(299, fields["f299"]);
    }

    [Fact]
    public async Task List_operations()
    {
        var key = Key("l");
        Assert.Equal(3, await Client.Lists.RightPushAsync(key, ["a", "b", "c"]));
        Assert.Equal(["a", "b", "c"], await Strings(Client.Lists.RangeAsync(key)));
        Assert.Equal(4, await Client.Lists.LeftPushAsync(key, "z"));
        Assert.Equal(0, await Client.Lists.LeftPushAsync(Key("none"), "x", When.Exists));
        Assert.Equal(5, await Client.Lists.RightPushAsync(key, "d", When.Exists));
        Assert.Equal(5, await Client.Lists.LengthAsync(key));
        Assert.Equal("b", (string?)await Client.Lists.GetByIndexAsync(key, 2));
        await Client.Lists.SetByIndexAsync(key, 2, "B");
        Assert.Equal(6, await Client.Lists.InsertBeforeAsync(key, "B", "pre"));
        Assert.Equal(7, await Client.Lists.InsertAfterAsync(key, "B", "post"));
        Assert.Equal(-1, await Client.Lists.InsertAfterAsync(key, "missing", "x"));
        Assert.Equal(["z", "a", "pre", "B", "post", "c", "d"], await Strings(Client.Lists.RangeAsync(key)));
        Assert.Equal(3, await Client.Lists.PositionAsync(key, "B"));
        Assert.Null(await Client.Lists.PositionAsync(key, "missing"));
        await Client.Lists.RightPushAsync(key, ["B", "B"]);
        var positions = await Client.Lists.PositionsAsync(key, "B", 0);
        Assert.Equal([3, 7, 8], positions);
        Assert.Equal(2, await Client.Lists.RemoveAsync(key, "B", -2));
        await Client.Lists.TrimAsync(key, 1, 4);
        Assert.Equal(["a", "pre", "B", "post"], await Strings(Client.Lists.RangeAsync(key)));
        Assert.Equal("a", (string?)await Client.Lists.LeftPopAsync(key));
        Assert.Equal("post", (string?)await Client.Lists.RightPopAsync(key));
        Assert.Equal(["pre", "B"], await Strings(Client.Lists.LeftPopAsync(key, 5)));
        Assert.True((await Client.Lists.LeftPopAsync(key)).IsNull);
        Assert.Empty(await Client.Lists.RightPopAsync(key, 2));
        await Assert.ThrowsAsync<RedisServerException>(() => Client.Lists.SetByIndexAsync(key, 10, "x").AsTask());
    }

    [Fact]
    public async Task List_move_and_blocking_pops()
    {
        var source = Key("src");
        var destination = Key("dst");
        await Client.Lists.RightPushAsync(source, ["1", "2", "3"]);
        Assert.Equal("1", (string?)await Client.Lists.MoveAsync(source, destination, ListSide.Left, ListSide.Right));
        Assert.Equal("3", (string?)await Client.Lists.BlockingMoveAsync(source, destination, ListSide.Right, ListSide.Left, TimeSpan.FromSeconds(1)));
        Assert.Equal(["3", "1"], await Strings(Client.Lists.RangeAsync(destination)));

        Assert.Equal("2", (string?)await Client.Lists.BlockingLeftPopAsync(source, TimeSpan.FromSeconds(1)));
        Assert.True((await Client.Lists.BlockingRightPopAsync(source, TimeSpan.FromMilliseconds(100))).IsNull);
        var popped = await Client.Lists.BlockingRightPopAsync([source, destination], TimeSpan.FromSeconds(1));
        Assert.NotNull(popped);
        Assert.Equal(destination, popped.Value.Key);
        Assert.Equal("1", (string?)popped.Value.Value);
        Assert.Null(await Client.Lists.BlockingLeftPopAsync([source], TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task Blocking_pop_waits_longer_than_command_timeout()
    {
        var key = Key("q");
        await using var client = new RedisClient(Fixture.Server.CreateOptions(o => o.CommandTimeout = TimeSpan.FromSeconds(1)));
        var pop = client.Lists.BlockingLeftPopAsync(key, TimeSpan.FromSeconds(4)).AsTask();
        await Task.Delay(2000);
        await Client.Lists.RightPushAsync(key, "item");
        Assert.Equal("item", (string?)await pop);
    }

    [Fact]
    public async Task Set_operations()
    {
        var key = Key("s");
        Assert.True(await Client.Sets.AddAsync(key, "a"));
        Assert.False(await Client.Sets.AddAsync(key, "a"));
        Assert.Equal(2, await Client.Sets.AddAsync(key, ["b", "c"]));
        Assert.Equal(["a", "b", "c"], (await Client.Sets.MembersAsync(key)).Select(v => (string)v!).Order());
        Assert.True(await Client.Sets.ContainsAsync(key, "a"));
        var contains = await Client.Sets.ContainsAsync(key, ["a", "x"]);
        Assert.Equal([true, false], contains);
        Assert.Equal(3, await Client.Sets.LengthAsync(key));
        Assert.False((await Client.Sets.RandomMemberAsync(key)).IsNull);
        Assert.Equal(2, (await Client.Sets.RandomMembersAsync(key, 2)).Length);
        Assert.True(await Client.Sets.MoveAsync(key, Key("other"), "c"));
        Assert.True(await Client.Sets.RemoveAsync(key, "b"));
        Assert.Equal(0, await Client.Sets.RemoveAsync(key, ["b", "x"]));
        Assert.Equal("a", (string?)await Client.Sets.PopAsync(key));
        Assert.True((await Client.Sets.PopAsync(key)).IsNull);
        await Client.Sets.AddAsync(key, ["1", "2", "3"]);
        Assert.Equal(3, (await Client.Sets.PopAsync(key, 5)).Length);
    }

    [Fact]
    public async Task Set_algebra_and_scan()
    {
        await Client.Sets.AddAsync(Key("x"), ["a", "b", "c"]);
        await Client.Sets.AddAsync(Key("y"), ["b", "c", "d"]);
        Assert.Equal(["a", "b", "c", "d"], (await Client.Sets.CombineAsync(SetOperation.Union, [Key("x"), Key("y")])).Select(v => (string)v!).Order());
        Assert.Equal(["b", "c"], (await Client.Sets.CombineAsync(SetOperation.Intersect, [Key("x"), Key("y")])).Select(v => (string)v!).Order());
        Assert.Equal(["a"], (await Client.Sets.CombineAsync(SetOperation.Difference, [Key("x"), Key("y")])).Select(v => (string)v!));
        Assert.Equal(2, await Client.Sets.CombineAndStoreAsync(SetOperation.Intersect, Key("z"), [Key("x"), Key("y")]));

        await Client.Sets.AddAsync(Key("big"), Enumerable.Range(0, 500).Select(i => (RedisValue)i).ToArray());
        var count = 0;
        await foreach (var _ in Client.Sets.ScanAsync(Key("big"), pageSize: 100))
        {
            count++;
        }

        Assert.Equal(500, count);
    }

    [Fact]
    public async Task Sorted_set_basics()
    {
        var key = Key("z");
        Assert.True(await Client.SortedSets.AddAsync(key, "a", 5));
        Assert.Equal(2, await Client.SortedSets.AddAsync(key, [new("b", 2), new("c", 3)]));
        Assert.False(await Client.SortedSets.AddAsync(key, "a", 3, SortedSetWhen.GreaterThan));
        Assert.Equal(5, await Client.SortedSets.ScoreAsync(key, "a"));
        Assert.True(await Client.SortedSets.AddAsync(key, "a", 6, SortedSetWhen.GreaterThan, countChanged: true));
        Assert.False(await Client.SortedSets.AddAsync(key, "new", 1, SortedSetWhen.Exists));
        Assert.Null(await Client.SortedSets.ScoreAsync(key, "new"));
        var scores = await Client.SortedSets.ScoresAsync(key, ["a", "new"]);
        Assert.Equal<double?>([6, null], scores);
        Assert.Equal(6.5, await Client.SortedSets.IncrementAsync(key, "a", 0.5));
        Assert.Equal(0, await Client.SortedSets.RankAsync(key, "b"));
        Assert.Equal(0, await Client.SortedSets.RankAsync(key, "a", Order.Descending));
        Assert.Null(await Client.SortedSets.RankAsync(key, "missing"));
        Assert.Equal(3, await Client.SortedSets.LengthAsync(key));
        Assert.Throws<ArgumentException>(() => { _ = Client.SortedSets.AddAsync(key, "x", 1, SortedSetWhen.NotExists | SortedSetWhen.GreaterThan); });
    }

    [Fact]
    public async Task Sorted_set_ranges()
    {
        var key = Key("r");
        await Client.SortedSets.AddAsync(key, [new("a", 1), new("b", 2), new("c", 3)]);
        Assert.Equal([new SortedSetEntry("b", 2), new SortedSetEntry("c", 3)],
            await Client.SortedSets.RangeByScoreWithScoresAsync(key, ScoreBound.Exclusive(1), ScoreBound.PositiveInfinity));
        Assert.Equal(["c", "b"], await Strings(Client.SortedSets.RangeByScoreAsync(key, 2, ScoreBound.PositiveInfinity, Order.Descending)));
        Assert.Equal(["b"], await Strings(Client.SortedSets.RangeByScoreAsync(key, ScoreBound.NegativeInfinity, ScoreBound.PositiveInfinity, offset: 1, count: 1)));
        Assert.Equal(["a", "b", "c"], await Strings(Client.SortedSets.RangeByRankAsync(key)));
        Assert.Equal([new SortedSetEntry("c", 3)], await Client.SortedSets.RangeByRankWithScoresAsync(key, 0, 0, Order.Descending));
        Assert.Equal(2, await Client.SortedSets.CountAsync(key, 2, 3));
        Assert.Equal(1, await Client.SortedSets.CountAsync(key, ScoreBound.Exclusive(2), 3));

        var lex = Key("lex");
        await Client.SortedSets.AddAsync(lex, [new("a", 0), new("b", 0), new("c", 0), new("d", 0)]);
        Assert.Equal(["b", "c"], await Strings(Client.SortedSets.RangeByLexAsync(lex, LexBound.Exclusive("a"), LexBound.Inclusive("c"))));
        Assert.Equal(["d", "c", "b", "a"], await Strings(Client.SortedSets.RangeByLexAsync(lex, LexBound.Min, LexBound.Max, Order.Descending)));

        Assert.Equal(2, await Client.SortedSets.RangeByRankAndStoreAsync(Key("store1"), key, 0, 1));
        Assert.Equal(1, await Client.SortedSets.RangeByScoreAndStoreAsync(Key("store2"), key, 3, 3));
        Assert.Equal(2, await Client.SortedSets.RangeByLexAndStoreAsync(Key("store3"), lex, LexBound.Min, LexBound.Exclusive("c")));

        Assert.Equal(1, await Client.SortedSets.RemoveRangeByRankAsync(Key("store1"), 0, 0));
        Assert.Equal(1, await Client.SortedSets.RemoveRangeByScoreAsync(Key("store2"), ScoreBound.NegativeInfinity, ScoreBound.PositiveInfinity));
        Assert.Equal(2, await Client.SortedSets.RemoveRangeByLexAsync(Key("store3"), LexBound.Min, LexBound.Max));
        Assert.True(await Client.SortedSets.RemoveAsync(key, "a"));
        Assert.Equal(1, await Client.SortedSets.RemoveAsync(key, ["b", "zzz"]));
    }

    [Fact]
    public async Task Sorted_set_pops()
    {
        var key = Key("p");
        await Client.SortedSets.AddAsync(key, [new("a", 1), new("b", 2), new("c", 3), new("d", 4)]);
        Assert.Equal(new SortedSetEntry("a", 1), await Client.SortedSets.PopMinAsync(key));
        Assert.Equal(new SortedSetEntry("d", 4), await Client.SortedSets.PopMaxAsync(key));
        Assert.Equal([new SortedSetEntry("b", 2)], await Client.SortedSets.PopMinAsync(key, 1));
        Assert.Equal([new SortedSetEntry("c", 3)], await Client.SortedSets.PopMaxAsync(key, 5));
        Assert.Null(await Client.SortedSets.PopMinAsync(key));
        Assert.Empty(await Client.SortedSets.PopMaxAsync(key, 2));

        await Client.SortedSets.AddAsync(key, "x", 7);
        var popped = await Client.SortedSets.BlockingPopMaxAsync([Key("none"), key], TimeSpan.FromSeconds(1));
        Assert.Equal((key, new SortedSetEntry("x", 7)), popped);
        Assert.Null(await Client.SortedSets.BlockingPopMinAsync([key], TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task Sorted_set_algebra_and_scan()
    {
        await Client.SortedSets.AddAsync(Key("x"), [new("a", 1), new("b", 2)]);
        await Client.SortedSets.AddAsync(Key("y"), [new("b", 10), new("c", 20)]);
        Assert.Equal(["a", "b", "c"], await Strings(Client.SortedSets.CombineAsync(SetOperation.Union, [Key("x"), Key("y")])));
        Assert.Equal([new SortedSetEntry("b", 22)], await Client.SortedSets.CombineWithScoresAsync(SetOperation.Intersect, [Key("x"), Key("y")], [1, 2]));
        Assert.Equal([new SortedSetEntry("b", 10)], await Client.SortedSets.CombineWithScoresAsync(SetOperation.Intersect, [Key("x"), Key("y")], aggregate: Aggregate.Max));
        Assert.Equal(["a"], await Strings(Client.SortedSets.CombineAsync(SetOperation.Difference, [Key("x"), Key("y")])));
        Assert.Equal(3, await Client.SortedSets.CombineAndStoreAsync(SetOperation.Union, Key("u"), [Key("x"), Key("y")]));
        Assert.Throws<ArgumentException>(() => { _ = Client.SortedSets.CombineAsync(SetOperation.Difference, [Key("x")], [1]); });

        await Client.SortedSets.AddAsync(Key("big"), Enumerable.Range(0, 300).Select(i => new SortedSetEntry($"m{i}", i)).ToArray());
        var entries = new List<SortedSetEntry>();
        await foreach (var entry in Client.SortedSets.ScanAsync(Key("big"), pattern: "m1*"))
        {
            entries.Add(entry);
        }

        Assert.Equal(111, entries.Count);
        Assert.Contains(new SortedSetEntry("m150", 150), entries);
    }

    /// <summary>Values as text; nil is rendered as <c>(nil)</c>.</summary>
    private static async Task<string[]> Strings(ValueTask<RedisValue[]> values) => [.. (await values).Select(v => v.ToString())];
}

[Collection(RedisCollection.Name)]
public sealed class CollectionCommandsResp2(RedisFixture fixture) : CollectionCommandTests(fixture, RedisProtocol.Resp2);

[Collection(RedisCollection.Name)]
public sealed class CollectionCommandsResp3(RedisFixture fixture) : CollectionCommandTests(fixture, RedisProtocol.Resp3);
