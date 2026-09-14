using Payment.Infrastructure.Caching;

namespace Payment.Tests.Infrastructure;

/// <summary>
/// Phase 11 — verifies the single-process cache and lock implementations
/// that back the Redis abstractions when Redis is not configured.
/// </summary>
public class InMemoryCachingTests
{
    [Fact]
    public async Task Cache_SetAndGet_RoundTrips()
    {
        var cache = new InMemoryCacheService();

        await cache.SetAsync("key1", "value1", TimeSpan.FromMinutes(1));

        var result = await cache.GetAsync<string>("key1");
        Assert.Equal("value1", result);
    }

    [Fact]
    public async Task Cache_MissingKey_ReturnsDefault()
    {
        var cache = new InMemoryCacheService();

        Assert.Null(await cache.GetAsync<string>("missing"));
        Assert.Equal(0, await cache.GetAsync<int>("missing"));
    }

    [Fact]
    public async Task Cache_ExpiredEntry_ReturnsDefault()
    {
        var cache = new InMemoryCacheService();

        await cache.SetAsync("key1", "value1", TimeSpan.FromMilliseconds(50));
        await Task.Delay(120);

        Assert.Null(await cache.GetAsync<string>("key1"));
    }

    [Fact]
    public async Task Cache_Remove_InvalidatesEntry()
    {
        var cache = new InMemoryCacheService();

        await cache.SetAsync("key1", "value1", TimeSpan.FromMinutes(1));
        await cache.RemoveAsync("key1");

        Assert.Null(await cache.GetAsync<string>("key1"));
    }

    [Fact]
    public async Task Cache_GetOrSet_UsesFactoryOnce()
    {
        var cache = new InMemoryCacheService();
        var factoryCalls = 0;

        var first = await cache.GetOrSetAsync("key1", () =>
        {
            factoryCalls++;
            return Task.FromResult("computed");
        }, TimeSpan.FromMinutes(1));

        var second = await cache.GetOrSetAsync("key1", () =>
        {
            factoryCalls++;
            return Task.FromResult("computed-again");
        }, TimeSpan.FromMinutes(1));

        Assert.Equal("computed", first);
        Assert.Equal("computed", second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task DistributedLock_SecondAcquire_FailsUntilReleased()
    {
        var locker = new InMemoryDistributedLock();

        var first = await locker.TryAcquireAsync("resource", TimeSpan.FromSeconds(30));
        Assert.NotNull(first);

        // While held, a second attempt must fail (returns null), NOT block.
        var second = await locker.TryAcquireAsync("resource", TimeSpan.FromSeconds(30));
        Assert.Null(second);

        await first!.DisposeAsync();

        // After release the resource is available again.
        var third = await locker.TryAcquireAsync("resource", TimeSpan.FromSeconds(30));
        Assert.NotNull(third);
        await third!.DisposeAsync();
    }

    [Fact]
    public async Task DistributedLock_DifferentKeys_DoNotInterfere()
    {
        var locker = new InMemoryDistributedLock();

        var a = await locker.TryAcquireAsync("resource-a", TimeSpan.FromSeconds(30));
        var b = await locker.TryAcquireAsync("resource-b", TimeSpan.FromSeconds(30));

        Assert.NotNull(a);
        Assert.NotNull(b);

        await a!.DisposeAsync();
        await b!.DisposeAsync();
    }
}
