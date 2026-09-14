using System.Text.Json;
using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Caching;
using Payment.Infrastructure.Redis;

namespace Payment.Infrastructure.Caching;

/// <summary>
/// Phase 11 — Redis-backed cache (cache-aside).
///
/// Failure policy: every Redis error is swallowed and logged. Callers then
/// transparently fall back to the database. A dead cache must never take
/// the payment API down and must never serve as source of truth.
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly RedisConnectionFactory _connectionFactory;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(RedisConnectionFactory connectionFactory, ILogger<RedisCacheService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connectionFactory.GetConnectionAsync(cancellationToken);
            if (connection is null)
                return default;

            var value = await connection.GetDatabase().StringGetAsync(_connectionFactory.PrefixedKey(key));
            if (value.IsNullOrEmpty)
                return default;

            return JsonSerializer.Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache GET failed for {Key}; falling through to database.", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connectionFactory.GetConnectionAsync(cancellationToken);
            if (connection is null)
                return;

            var json = JsonSerializer.Serialize(value);
            await connection.GetDatabase().StringSetAsync(_connectionFactory.PrefixedKey(key), json, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache SET failed for {Key}; continuing without caching.", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connectionFactory.GetConnectionAsync(cancellationToken);
            if (connection is null)
                return;

            await connection.GetDatabase().KeyDeleteAsync(_connectionFactory.PrefixedKey(key));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache REMOVE failed for {Key}.", key);
        }
    }

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null)
            return cached;

        var value = await factory();
        await SetAsync(key, value, ttl, cancellationToken);
        return value;
    }
}
