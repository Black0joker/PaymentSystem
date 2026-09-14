namespace Payment.Application.Abstractions.Caching;

/// <summary>
/// Null-object implementations used when no cache/lock store is configured
/// (tests, minimal deployments). They preserve correctness by always
/// falling through to the authoritative database path.
/// </summary>
public sealed class NoOpCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<T?>(default);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        await factory();
}

public sealed class NoOpDistributedLock : IDistributedLock
{
    private sealed class Handle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAsyncDisposable?>(new Handle());
}
