namespace Payment.Application.Abstractions.RateLimiting;

/// <summary>
/// Phase 11 — distributed rate limiter abstraction (fixed window per key).
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to consume one request unit for the given key within the
    /// given window. Never throws into callers (fail-open).
    /// </summary>
    Task<RateLimitDecision> TryAcquireAsync(
        string key,
        int maxRequests,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}

/// <param name="Allowed">Whether the request may proceed.</param>
/// <param name="Remaining">Requests remaining in the current window.</param>
/// <param name="RetryAfter">Suggested wait time when the request was rejected.</param>
public record RateLimitDecision(bool Allowed, int Remaining, TimeSpan? RetryAfter);
