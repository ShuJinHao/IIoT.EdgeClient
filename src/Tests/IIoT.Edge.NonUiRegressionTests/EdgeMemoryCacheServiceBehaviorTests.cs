using IIoT.Edge.Infrastructure.Persistence.EfCore.Caching.Memory;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class EdgeMemoryCacheServiceBehaviorTests
{
    [Fact]
    public async Task GetOrCreateAsync_WhenCacheHit_ShouldNotCallFactory()
    {
        var cache = new EdgeMemoryCacheService();
        cache.Set("demo", "cached");

        var value = await cache.GetOrCreateAsync<string>(
            "demo",
            _ => Task.FromResult<string?>("factory"));

        Assert.Equal("cached", value);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenSameKeyConcurrent_ShouldCallFactoryOnce()
    {
        var cache = new EdgeMemoryCacheService();
        var calls = 0;

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => cache.GetOrCreateAsync<int>(
                "shared",
                async _ =>
                {
                    Interlocked.Increment(ref calls);
                    await Task.Delay(20);
                    return 7;
                }))
            .ToArray();

        var values = await Task.WhenAll(tasks);

        Assert.All(values, value => Assert.Equal(7, value));
        Assert.Equal(1, calls);
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
            });
        var second = await cache.GetOrCreateAsync<string>(
            "missing",
            _ =>
            {
                calls++;
                return Task.FromResult<string?>("unexpected");
            });

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(1, calls);
        Assert.True(cache.Contains("missing"));
    }

    [Fact]
    public async Task RemoveByPrefix_WhenEntryCreatedByGetOrCreate_ShouldRemoveIt()
    {
        var cache = new EdgeMemoryCacheService();
        await cache.GetOrCreateAsync("Param:Module:Homogenization", _ => Task.FromResult<string?>("value"));

        cache.RemoveByPrefix("Param:Module:");

        Assert.False(cache.Contains("Param:Module:Homogenization"));
    }
}
