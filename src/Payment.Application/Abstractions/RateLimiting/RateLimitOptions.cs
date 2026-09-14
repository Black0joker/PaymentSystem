namespace Payment.Application.Abstractions.RateLimiting;

/// <summary>
/// Configuration for API rate limiting. Bound from the "RateLimiting"
/// configuration section.
///
/// Fixed window per client IP + path prefix. When the budget is exhausted
/// the API answers 429 with a Retry-After header instead of degrading.
/// </summary>
public class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    /// <summary>Maximum requests per client IP per path prefix within the window.</summary>
    public int MaxRequests { get; set; } = 30;

    /// <summary>Window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Path prefixes the limiter applies to. Provider webhook endpoints are
    /// intentionally NOT rate limited — the provider's retries are legitimate
    /// traffic, and rejecting them would delay payment confirmation.
    /// </summary>
    public string[] ApplyToPathPrefixes { get; set; } = { "/api/checkout", "/api/payments" };
}
