using System.Collections.Concurrent;
using Payment.Application.Abstractions.Caching;

namespace Payment.Infrastructure.Caching;

/// <summary>
/// Single-process cache used when Redis is not configured
/// (local development, tests). Same contract, smaller scope.
/// </summary>
public class InMemoryCacheService : ICacheService
{
    private sealed record Entry(object? Value, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new();

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.ExpiresAt > DateTime.UtcNow)
                return Task.FromResult((T?)entry.Value);

            _store.TryRemove(key, out _);
        }

        return Task.FromResult<T?>(default);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        _store[key] = new Entry(value, DateTime.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
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
