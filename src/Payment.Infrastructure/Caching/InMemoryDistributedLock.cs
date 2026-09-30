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

    public Task<LockAcquisitionResult> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        // Non-blocking attempt: Busy means another request holds the lock.
        // The in-memory store itself is always available, so Unavailable never occurs here.
        var acquired = semaphore.Wait(0, cancellationToken);
        return Task.FromResult(acquired
            ? new LockAcquisitionResult(LockAcquireStatus.Acquired, new Handle(semaphore))
            : new LockAcquisitionResult(LockAcquireStatus.Busy, null));
    }
}
