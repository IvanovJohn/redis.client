using Redis.Client.IntegrationTests.Infrastructure;

namespace Redis.Client.IntegrationTests;

[Collection(RedisCollection.Name)]
public sealed class TransactionTests(RedisFixture fixture) : IAsyncLifetime
{
    private RedisClient _client = null!;

    public Task InitializeAsync()
    {
        _client = new RedisClient(fixture.Server.CreateOptions());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _client.DisposeAsync();

    [Fact]
    public async Task Queued_commands_complete_after_exec()
    {
        var prefix = RedisFixture.Prefix();
        var tx = _client.CreateTransaction();
        var a = tx.Strings.IncrementAsync(prefix + "a").AsTask();
        var b = tx.Strings.IncrementAsync(prefix + "b", 5).AsTask();
        var get = tx.Strings.GetAsync(prefix + "a").AsTask();
        Assert.False(a.IsCompleted);

        await tx.ExecuteAsync();
        Assert.Equal(1, await a);
        Assert.Equal(5, await b);
        Assert.Equal(1, (long)await get);
    }

    [Fact]
    public async Task Transactions_are_atomic_under_concurrency()
    {
        var counter = RedisFixture.Prefix() + "counter";
        var noise = RedisFixture.Prefix() + "noise";
        using var stop = new CancellationTokenSource();
        var background = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await _client.Strings.IncrementAsync(noise);
            }
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(async _ =>
        {
            var tx = _client.CreateTransaction();
            var incremented = tx.Strings.IncrementAsync(counter);
            var read = tx.Strings.GetAsync(counter);
            await tx.ExecuteAsync();
            return (await incremented, (long)await read);
        }));

        await stop.CancelAsync();
        await background;
        Assert.All(results, r => Assert.Equal(r.Item1, r.Item2));
        Assert.Equal(Enumerable.Range(1, 100).Select(i => (long)i), results.Select(r => r.Item1).Order());
    }

    [Fact]
    public async Task Queue_error_aborts_the_whole_transaction()
    {
        var key = RedisFixture.Prefix() + "k";
        var tx = _client.CreateTransaction();
        var set = tx.Strings.SetAsync(key, "v").AsTask();
        var bad = tx.ExecuteAsync("SET", [key]).AsTask(); // wrong number of arguments: rejected while queuing
        var error = await Assert.ThrowsAsync<RedisServerException>(() => tx.ExecuteAsync().AsTask());
        Assert.Equal("EXECABORT", error.ErrorCode);
        Assert.Equal("EXECABORT", (await Assert.ThrowsAsync<RedisServerException>(() => set)).ErrorCode);
        Assert.Equal("EXECABORT", (await Assert.ThrowsAsync<RedisServerException>(() => bad)).ErrorCode);
        Assert.False(await _client.Keys.ExistsAsync(key));
    }

    [Fact]
    public async Task Runtime_error_affects_only_its_command()
    {
        var key = RedisFixture.Prefix() + "k";
        var tx = _client.CreateTransaction();
        var set = tx.Strings.SetAsync(key, "v").AsTask();
        var push = tx.Lists.LeftPushAsync(key, "x").AsTask();
        await tx.ExecuteAsync();
        Assert.True(await set);
        Assert.Equal("WRONGTYPE", (await Assert.ThrowsAsync<RedisServerException>(() => push)).ErrorCode);
    }

    [Fact]
    public async Task Transaction_that_is_not_executed_sends_nothing()
    {
        var key = RedisFixture.Prefix() + "k";
        var tx = _client.CreateTransaction();
        _ = tx.Strings.SetAsync(key, "v");
        await Task.Delay(100);
        Assert.False(await _client.Keys.ExistsAsync(key));
    }

    [Fact]
    public async Task Transaction_cannot_be_executed_twice_and_rejects_scan()
    {
        var tx = _client.CreateTransaction();
        _ = tx.Strings.SetAsync(RedisFixture.Prefix(), "v");
        await tx.ExecuteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => tx.ExecuteAsync().AsTask());
        Assert.Throws<InvalidOperationException>(() => { _ = tx.Strings.GetAsync("k"); });

        var scanning = _client.CreateTransaction();
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in scanning.Keys.ScanAsync())
            {
            }
        });
    }

    [Fact]
    public async Task Watched_transaction_succeeds_without_conflict()
    {
        var balance = RedisFixture.Prefix() + "balance";
        await _client.Strings.SetAsync(balance, 100);
        await using var tx = await _client.BeginWatchedTransactionAsync([balance]);
        var current = (long)await tx.Connection.Strings.GetAsync(balance);
        var set = tx.Strings.SetAsync(balance, current - 30).AsTask();
        Assert.True(await tx.ExecuteAsync());
        Assert.True(await set);
        Assert.Equal(70, (long)await _client.Strings.GetAsync(balance));
    }

    [Fact]
    public async Task Watched_transaction_detects_conflict()
    {
        var balance = RedisFixture.Prefix() + "balance";
        await _client.Strings.SetAsync(balance, 100);
        await using var tx = await _client.BeginWatchedTransactionAsync([balance]);
        var current = (long)await tx.Connection.Strings.GetAsync(balance);

        await fixture.Server.CliAsync("SET", balance, "500");
        var set = tx.Strings.SetAsync(balance, current - 30).AsTask();
        Assert.False(await tx.ExecuteAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => set);
        Assert.Equal(500, (long)await _client.Strings.GetAsync(balance));
    }

    [Fact]
    public async Task Disposed_watched_transaction_returns_a_clean_connection()
    {
        var key = RedisFixture.Prefix() + "watched";
        await using var client = new RedisClient(fixture.Server.CreateOptions(o => o.DedicatedPoolSize = 1));
        await using (await client.BeginWatchedTransactionAsync([key]))
        {
        }

        // Same pooled connection: a change to the previously watched key must not abort this transaction.
        await using var tx = await client.BeginWatchedTransactionAsync([RedisFixture.Prefix() + "other"]);
        await client.Strings.SetAsync(key, "changed");
        _ = tx.Strings.SetAsync(key, "mine");
        Assert.True(await tx.ExecuteAsync());
        Assert.Equal(1, client.Router.Dedicated.InUse);
    }
}
