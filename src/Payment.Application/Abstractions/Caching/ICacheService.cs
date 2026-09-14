namespace Payment.Application.Abstractions.Caching;

/// <summary>
/// Phase 11 — distributed cache abstraction.
///
/// Invariants (PLAN §58):
///  - The cache is an OPTIMIZATION, never the source of truth.
///  - SQL Server remains authoritative for all financial state.
///  - Every read path must tolerate a cache miss or a cache failure by
///    falling back to the database.
///  - Cache entries are short-lived (seconds) so staleness stays bounded.
/// </summary>
public interface ICacheService
{
    /// <summary>Returns the cached value or default(T) when absent.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores a value with a TTL. Must never throw into callers.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Removes a key (used for invalidation on state changes).</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache-aside helper: return the cached value or compute it via the
    /// factory, cache the result, and return it.
    /// </summary>
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl, CancellationToken cancellationToken = default);
}
