using Microsoft.Extensions.Options;
using Payment.Application.Abstractions.RateLimiting;

namespace Payment.Api.Middleware;

/// <summary>
/// Phase 11 — fixed-window rate limiting per client IP and path prefix.
///
/// Protects capacity of client-initiated endpoints (checkout creation,
/// refund requests). Provider webhook endpoints are deliberately excluded:
/// provider retries are legitimate traffic and rejecting them would delay
/// authoritative payment confirmation.
///
/// The limiter itself fails open (IRateLimiter contract): if the backing
/// store is down, requests proceed — correctness is protected by domain
/// guards and DB constraints, not by this middleware.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimiter _rateLimiter;
    private readonly RateLimitOptions _options;

    public RateLimitingMiddleware(
        RequestDelegate next,
        IRateLimiter rateLimiter,
        IOptions<RateLimitOptions> options)
    {
        _next = next;
        _rateLimiter = rateLimiter;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var prefix = MatchingPrefix(context.Request.Path);
        if (prefix is null)
        {
            await _next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var key = $"{clientIp}:{context.Request.Method}:{prefix}";

        var decision = await _rateLimiter.TryAcquireAsync(
            key,
            _options.MaxRequests,
            TimeSpan.FromSeconds(_options.WindowSeconds),
            context.RequestAborted);

        if (!decision.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

            if (decision.RetryAfter is { } retryAfter)
            {
                context.Response.Headers.RetryAfter =
                    Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
            }

            await context.Response.WriteAsync("Rate limit exceeded. Try again later.");
            return;
        }

        await _next(context);
    }

    private string? MatchingPrefix(PathString path)
    {
        foreach (var prefix in _options.ApplyToPathPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return prefix;
        }

        return null;
    }
}
