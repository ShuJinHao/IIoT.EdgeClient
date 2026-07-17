using IIoT.Edge.Application.Common.Caching.Memory;

namespace IIoT.Edge.Caching.UnitTests;

public sealed class EdgeMemoryCacheServiceBehaviorTests
{
    [Fact]
    public async Task GetOrCreateAsync_WhenCacheHitAndCallerIsPreCanceled_ShouldPropagateWithoutFactory()
    {
        var cache = new EdgeMemoryCacheService();
        cache.Set("demo", "cached");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var calls = 0;

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync<string>(
            "demo",
            _ =>
            {
                calls++;
                return Task.FromResult<string?>("factory");
            },
            cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCallerCancelsAsFactoryReturns_ShouldNotWriteCache()
    {
        var cache = new EdgeMemoryCacheService();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;

        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetOrCreateAsync<string>(
            "demo",
            _ =>
            {
                calls++;
                cancellation.Cancel();
                return Task.FromResult<string?>("created");
            },
            cancellationToken: cancellation.Token));

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(1, calls);
        Assert.False(cache.Contains("demo"));
    }

    [Fact]
    public async Task CacheOperations_ShouldPreserveTypedExpirationAndFailureContracts()
    {
        var cache = new EdgeMemoryCacheService();

        cache.Set("first", "one");
        cache.Set<string?>("ignored-null", null);
        cache.Set("second", 2);

        Assert.Equal("one", cache.Get<string>("first"));
        Assert.Null(cache.Get<int?>("first"));
        Assert.False(cache.Contains("ignored-null"));

        cache.Remove("first");
        Assert.Null(cache.Get<string>("first"));

        await Assert.ThrowsAsync<ArgumentException>(() => cache.GetOrCreateAsync<string>(
            " ",
            _ => Task.FromResult<string?>("unused"),
            cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => cache.GetOrCreateAsync<string>(
            "valid",
            null!,
            cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrCreateAsync<string>(
            "retryable",
            _ => throw new InvalidOperationException("boom"),
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(
            "recovered",
            await cache.GetOrCreateAsync(
                "retryable",
                _ => Task.FromResult<string?>("recovered"),
                cancellationToken: TestContext.Current.CancellationToken));

        var valueCalls = 0;

        var first = await cache.GetOrCreateAsync(
            "expiring",
            _ => Task.FromResult<string?>(Interlocked.Increment(ref valueCalls).ToString()),
            absoluteExpirationRelativeToNow: TimeSpan.Zero,
            cancellationToken: TestContext.Current.CancellationToken);
        var second = await cache.GetOrCreateAsync(
            "expiring",
            _ => Task.FromResult<string?>(Interlocked.Increment(ref valueCalls).ToString()),
            absoluteExpirationRelativeToNow: TimeSpan.Zero,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("1", first);
        Assert.Equal("2", second);
        Assert.Equal(2, valueCalls);
        Assert.False(cache.Contains("expiring"));

        var nullCalls = 0;

        await cache.GetOrCreateAsync<string>(
            "immediate-null",
            _ =>
            {
                Interlocked.Increment(ref nullCalls);
                return Task.FromResult<string?>(null);
            },
            nullValueExpirationRelativeToNow: TimeSpan.Zero,
            cancellationToken: TestContext.Current.CancellationToken);
        await cache.GetOrCreateAsync<string>(
            "immediate-null",
            _ =>
            {
                Interlocked.Increment(ref nullCalls);
                return Task.FromResult<string?>(null);
            },
            nullValueExpirationRelativeToNow: TimeSpan.Zero,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, nullCalls);

        await cache.GetOrCreateAsync(
            "Param:Module:TestPlugin",
            _ => Task.FromResult<string?>("value"),
            cancellationToken: TestContext.Current.CancellationToken);
        cache.RemoveByPrefix("Param:Module:");
        Assert.False(cache.Contains("Param:Module:TestPlugin"));

        cache.Clear();
        Assert.False(cache.Contains("second"));
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCacheHit_ShouldNotCallFactory()
    {
        var cache = new EdgeMemoryCacheService();
        cache.Set("demo", "cached");

        var value = await cache.GetOrCreateAsync<string>(
            "demo",
            _ => Task.FromResult<string?>("factory"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("cached", value);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenSameKeyConcurrent_ShouldCallFactoryOnce()
    {
        var cache = new EdgeMemoryCacheService();
        var calls = 0;
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => cache.GetOrCreateAsync<int>(
                "shared",
                async cancellationToken =>
                {
                    Interlocked.Increment(ref calls);
                    factoryEntered.TrySetResult();
                    await releaseFactory.Task.WaitAsync(cancellationToken);
                    return 7;
                },
                cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        await factoryEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        releaseFactory.SetResult();
        var values = await Task.WhenAll(tasks);

        Assert.All(values, value => Assert.Equal(7, value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CacheKeys_ShouldUseOneCaseInsensitiveIdentityAcrossStorageLocksAndInvalidation()
    {
        var cache = new EdgeMemoryCacheService();
        cache.Set("Param:Module:TestPlugin", "seed");

        Assert.Equal("seed", cache.Get<string>("param:module:testplugin"));
        Assert.True(cache.Contains("PARAM:MODULE:TESTPLUGIN"));

        var calls = 0;
        cache.Remove("PARAM:MODULE:TESTPLUGIN");
        var first = cache.GetOrCreateAsync<string>(
            "Shared-Key",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<string?>("created");
            },
            cancellationToken: TestContext.Current.CancellationToken);
        var second = cache.GetOrCreateAsync<string>(
            "shared-key",
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult<string?>("unexpected");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(await Task.WhenAll(first, second), value => Assert.Equal("created", value));
        Assert.Equal(1, calls);

        cache.RemoveByPrefix("SHARED-");
        Assert.False(cache.Contains("shared-key"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredReadCleanup_WhenSetWinsInterleaving_ShouldNotDeleteFreshReplacement(bool useContains)
    {
        var expiredObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new EdgeMemoryCacheService(_ =>
        {
            expiredObserved.TrySetResult();
            releaseCleanup.Task.GetAwaiter().GetResult();
        });

        await cache.GetOrCreateAsync(
            "Race-Key",
            _ => Task.FromResult<string?>("expired"),
            absoluteExpirationRelativeToNow: TimeSpan.Zero,
            cancellationToken: TestContext.Current.CancellationToken);

        var expiredRead = Task.Run(
            () => useContains ? cache.Contains("race-key") : cache.Get<string>("race-key") is not null,
            TestContext.Current.CancellationToken);
        await expiredObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        cache.Set("RACE-KEY", "fresh");
        releaseCleanup.SetResult();

        Assert.False(await expiredRead);
        Assert.True(cache.Contains("race-key"));
        Assert.Equal("fresh", cache.Get<string>("RACE-KEY"));
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("prefix")]
    [InlineData("clear")]
    public async Task InvalidationDuringFactory_ShouldPreventStaleRepopulation(string invalidation)
    {
        var cache = new EdgeMemoryCacheService();
        const string key = "Param:Module:TestPlugin";
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var staleLoad = cache.GetOrCreateAsync<string>(
            key,
            async cancellationToken =>
            {
                factoryEntered.SetResult();
                await releaseFactory.Task.WaitAsync(cancellationToken);
                return "stale";
            },
            cancellationToken: TestContext.Current.CancellationToken);

        await factoryEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);
        switch (invalidation)
        {
            case "remove":
                cache.Remove(key);
                break;
            case "prefix":
                cache.RemoveByPrefix("Param:Module:");
                break;
            case "clear":
                cache.Clear();
                break;
        }

        releaseFactory.SetResult();
        Assert.Equal("stale", await staleLoad);
        Assert.False(cache.Contains(key));

        var fresh = await cache.GetOrCreateAsync<string>(
            key,
            _ => Task.FromResult<string?>("fresh"),
            cancellationToken: TestContext.Current.CancellationToken);
        var cached = await cache.GetOrCreateAsync<string>(
            key,
            _ => Task.FromResult<string?>("unexpected"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("fresh", fresh);
        Assert.Equal("fresh", cached);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenFactoryReturnsNull_ShouldCacheNullBriefly()
    {
        var cache = new EdgeMemoryCacheService();
        var calls = 0;

        var first = await cache.GetOrCreateAsync<string>(
            "missing",
            _ =>
            {
                calls++;
                return Task.FromResult<string?>(null);
            },
            cancellationToken: TestContext.Current.CancellationToken);
        var second = await cache.GetOrCreateAsync<string>(
            "missing",
            _ =>
            {
                calls++;
                return Task.FromResult<string?>("unexpected");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, calls);
        Assert.True(cache.Contains("missing"));
    }

}
