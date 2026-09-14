namespace Payment.Application.Abstractions.Caching;

/// <summary>
/// Phase 11 — distributed lock abstraction.
///
/// Used to serialize concurrent operations on the SAME resource (e.g. two
/// refund requests for one payment) as a defense-in-depth optimization.
///
/// Critical invariant: the lock is NOT the correctness mechanism.
/// Domain state-machine guards and database constraints remain authoritative.
/// If a lock cannot be acquired (or the lock store is down), the operation
/// MUST still proceed and rely on those guards.
/// </summary>
public interface IDistributedLock
{
    /// <summary>
    /// Attempts to acquire an exclusive lock.
    /// Returns a handle whose disposal releases the lock, or null when the
    /// lock is already held or the lock store is unavailable.
    /// Must never throw into callers.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);
}
