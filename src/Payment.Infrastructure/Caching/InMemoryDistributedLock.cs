using System.Collections.Concurrent;
using Payment.Application.Abstractions.Caching;

namespace Payment.Infrastructure.Caching;

/// <summary>
/// Single-process lock used when Redis is not configured.
/// Same contract as the Redis lock; expiry is not enforced in-process
/// (a process crash releases everything anyway).
/// </summary>
public class InMemoryDistributedLock : IDistributedLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private sealed class Handle : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _disposed;

        public Handle(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }

    public Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        // Non-blocking attempt: the caller must be able to fall through to
        // the domain guards when the resource is busy.
        var acquired = semaphore.Wait(0, cancellationToken);
        return Task.FromResult<IAsyncDisposable?>(acquired ? new Handle(semaphore) : null);
    }
}
