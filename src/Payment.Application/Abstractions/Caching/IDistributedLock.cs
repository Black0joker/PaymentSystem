namespace Payment.Application.Abstractions.Caching;

/// <summary>
/// Phase 11 — distributed lock abstraction.
///
/// Used to serialize concurrent operations on the SAME resource (e.g. two
/// refund requests for one payment) as a defense-in-depth optimization.
///
/// Critical invariant: the lock is NOT the correctness mechanism.
/// Domain state-machine guards and database constraints remain authoritative.
/// </summary>
public enum LockAcquireStatus
{
    /// <summary>Lock acquired; <see cref="LockAcquisitionResult.Handle"/> must be disposed to release.</summary>
    Acquired,

    /// <summary>Another holder owns the lock. Caller should fail fast (e.g. 409 Conflict), not retry inline.</summary>
    Busy,

    /// <summary>Lock store unreachable/error. Caller must fall back to DB guards and proceed.</summary>
    Unavailable
}

/// <summary>Outcome of a lock attempt. Handle is non-null only when Status is Acquired.</summary>
public sealed record LockAcquisitionResult(LockAcquireStatus Status, IAsyncDisposable? Handle);

public interface IDistributedLock
{
    /// <summary>
    /// Attempts to acquire an exclusive lock (non-blocking).
    /// Must never throw into callers (except OperationCanceledException).
    /// </summary>
    Task<LockAcquisitionResult> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default);
}
