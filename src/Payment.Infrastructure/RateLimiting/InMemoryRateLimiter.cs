using System.Collections.Concurrent;
using Payment.Application.Abstractions.RateLimiting;

namespace Payment.Infrastructure.RateLimiting;

/// <summary>
/// Single-process fixed-window rate limiter used when Redis is not
/// configured. Same contract; the window is per process instead of global.
/// </summary>
public class InMemoryRateLimiter : IRateLimiter
{
    private sealed class Window
    {
        public DateTime StartedAt;
        public int Count;
    }

    private readonly ConcurrentDictionary<string, Window> _windows = new();

    public Task<RateLimitDecision> TryAcquireAsync(
        string key,
        int maxRequests,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var entry = _windows.AddOrUpdate(
            key,
            _ => new Window { StartedAt = now, Count = 1 },
            (_, existing) =>
            {
                lock (existing)
                {
                    if (now - existing.StartedAt >= window)
                    {
                        existing.StartedAt = now;
                        existing.Count = 1;
                    }
                    else
                    {
                        existing.Count++;
                    }

                    return existing;
                }
            });

        int count;
        TimeSpan remaining;
        lock (entry)
        {
            count = entry.Count;
            remaining = window - (now - entry.StartedAt);
        }

        if (count > maxRequests)
            return Task.FromResult(new RateLimitDecision(false, 0, remaining < TimeSpan.Zero ? window : remaining));

        return Task.FromResult(new RateLimitDecision(true, maxRequests - count, null));
    }
}
