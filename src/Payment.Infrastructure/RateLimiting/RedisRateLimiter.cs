using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.RateLimiting;
using Payment.Infrastructure.Redis;
using StackExchange.Redis;

namespace Payment.Infrastructure.RateLimiting;

/// <summary>
/// Phase 11 — Redis-backed fixed-window rate limiter.
///
/// The counter increment and expiry are applied atomically by a Lua script,
/// which makes the limit exact across all API instances sharing the Redis:
///
///   INCR key                 — atomic per-key counter
///   PEXPIRE key window (once) — the first request opens the window
///   PTTL key                 — remaining window time for Retry-After
///
/// Fail-open policy: if Redis is unavailable the request is ALLOWED.
/// Rate limiting protects capacity; it must never block legitimate traffic
/// when the protection layer itself is down. SQL constraints still protect
/// correctness either way.
/// </summary>
public class RedisRateLimiter : IRateLimiter
{
    private const string RateLimitScript = @"
local current = redis.call('INCR', KEYS[1])
if current == 1 then
    redis.call('PEXPIRE', KEYS[1], ARGV[1])
end
local ttl = redis.call('PTTL', KEYS[1])
return { current, ttl }";

    private readonly RedisConnectionFactory _connectionFactory;
    private readonly ILogger<RedisRateLimiter> _logger;

    public RedisRateLimiter(RedisConnectionFactory connectionFactory, ILogger<RedisRateLimiter> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<RateLimitDecision> TryAcquireAsync(
        string key,
        int maxRequests,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connectionFactory.GetConnectionAsync(cancellationToken);
            if (connection is null)
                return new RateLimitDecision(true, maxRequests, null); // fail-open

            var fullKey = _connectionFactory.PrefixedKey("ratelimit:" + key);

            var scriptResult = await connection.GetDatabase().ScriptEvaluateAsync(
                RateLimitScript,
                new RedisKey[] { fullKey },
                new RedisValue[] { (long)window.TotalMilliseconds });

            var result = (RedisResult[]?)scriptResult
                ?? throw new InvalidOperationException("Rate limit script returned no result.");

            var count = (long)result[0];
            var ttlMs = (long)result[1];

            if (count > maxRequests)
            {
                return new RateLimitDecision(
                    false,
                    0,
                    TimeSpan.FromMilliseconds(Math.Max(ttlMs, 1)));
            }

            return new RateLimitDecision(true, maxRequests - (int)count, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rate limiter unavailable for {Key}; failing open.", key);
            return new RateLimitDecision(true, maxRequests, null);
        }
    }
}
