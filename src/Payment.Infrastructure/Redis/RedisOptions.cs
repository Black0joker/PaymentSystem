namespace Payment.Infrastructure.Redis;

/// <summary>
/// Configuration for the Redis-backed caching, locking and rate-limiting.
/// Bound from the "Redis" configuration section.
///
/// When ConnectionString is empty, in-memory implementations are used
/// instead (local development, tests) — behavior is identical, the scope
/// is a single process instead of the whole cluster.
/// </summary>
public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Redis connection string, e.g. "localhost:6379".
    /// Empty means: use in-memory fallbacks.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Key prefix so multiple apps can share one Redis instance safely.</summary>
    public string KeyPrefix { get; set; } = "paymentsystem:";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
