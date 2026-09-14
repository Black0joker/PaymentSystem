using Payment.Infrastructure.RateLimiting;

namespace Payment.Tests.Infrastructure;

/// <summary>
/// Phase 11 — fixed-window rate limiter behavior (single-process variant).
/// </summary>
public class InMemoryRateLimiterTests
{
    [Fact]
    public async Task Limiter_AllowsUpToLimit()
    {
        var limiter = new InMemoryRateLimiter();

        for (var i = 0; i < 5; i++)
        {
            var decision = await limiter.TryAcquireAsync("client", 5, TimeSpan.FromMinutes(1));
            Assert.True(decision.Allowed);
            Assert.Equal(5 - (i + 1), decision.Remaining);
        }
    }

    [Fact]
    public async Task Limiter_RejectsOverLimit_WithRetryAfter()
    {
        var limiter = new InMemoryRateLimiter();

        for (var i = 0; i < 3; i++)
            await limiter.TryAcquireAsync("client", 3, TimeSpan.FromMinutes(1));

        var rejected = await limiter.TryAcquireAsync("client", 3, TimeSpan.FromMinutes(1));

        Assert.False(rejected.Allowed);
        Assert.Equal(0, rejected.Remaining);
        Assert.NotNull(rejected.RetryAfter);
    }

    [Fact]
    public async Task Limiter_DifferentKeys_AreIndependent()
    {
        var limiter = new InMemoryRateLimiter();

        for (var i = 0; i < 3; i++)
            await limiter.TryAcquireAsync("client-a", 3, TimeSpan.FromMinutes(1));

        // client-a exhausted its budget; client-b must be unaffected
        var decision = await limiter.TryAcquireAsync("client-b", 3, TimeSpan.FromMinutes(1));
        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Limiter_WindowExpiry_ResetsBudget()
    {
        var limiter = new InMemoryRateLimiter();

        for (var i = 0; i < 2; i++)
            await limiter.TryAcquireAsync("client", 2, TimeSpan.FromMilliseconds(100));

        var rejected = await limiter.TryAcquireAsync("client", 2, TimeSpan.FromMilliseconds(100));
        Assert.False(rejected.Allowed);

        await Task.Delay(150);

        var allowed = await limiter.TryAcquireAsync("client", 2, TimeSpan.FromMilliseconds(100));
        Assert.True(allowed.Allowed);
    }
}
