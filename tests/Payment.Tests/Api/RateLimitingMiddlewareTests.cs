using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Payment.Api.Middleware;
using Payment.Application.Abstractions.RateLimiting;
using Payment.Infrastructure.RateLimiting;

namespace Payment.Tests.Api;

/// <summary>
/// Phase 11 — HTTP-level rate limiting behavior:
///  - configured prefixes are limited per client IP
///  - exceeding the budget produces 429 + Retry-After
///  - unlisted paths (e.g. provider webhooks) are never limited
///  - limits fail open when the limiter allows everything
/// </summary>
public class RateLimitingMiddlewareTests
{
    private static readonly RateLimitOptions TestOptions = new()
    {
        Enabled = true,
        MaxRequests = 2,
        WindowSeconds = 60,
        ApplyToPathPrefixes = new[] { "/api/payments", "/api/checkout" }
    };

    private static RateLimitingMiddleware CreateMiddleware(IRateLimiter limiter, RequestDelegate next)
    {
        return new RateLimitingMiddleware(next, limiter, Options.Create(TestOptions));
    }

    private static DefaultHttpContext CreateContext(string path, string ip, string method = "POST")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task Requests_UnderLimit_PassThrough()
    {
        var nextCalled = 0;
        var middleware = CreateMiddleware(new InMemoryRateLimiter(), _ =>
        {
            nextCalled++;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext("/api/payments", "10.0.0.1"));
        await middleware.InvokeAsync(CreateContext("/api/payments", "10.0.0.1"));

        Assert.Equal(2, nextCalled);
    }

    [Fact]
    public async Task Requests_OverLimit_Get429WithRetryAfter()
    {
        var nextCalled = 0;
        var middleware = CreateMiddleware(new InMemoryRateLimiter(), _ =>
        {
            nextCalled++;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext("/api/payments", "10.0.0.2"));
        await middleware.InvokeAsync(CreateContext("/api/payments", "10.0.0.2"));

        var rejected = CreateContext("/api/payments", "10.0.0.2");
        await middleware.InvokeAsync(rejected);

        Assert.Equal(2, nextCalled); // third request never reached the pipeline
        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        Assert.True(rejected.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task UnlistedPaths_AreNeverLimited()
    {
        var nextCalled = 0;
        var middleware = CreateMiddleware(new InMemoryRateLimiter(), _ =>
        {
            nextCalled++;
            return Task.CompletedTask;
        });

        // Provider webhook path is deliberately outside the limited prefixes
        for (var i = 0; i < 10; i++)
            await middleware.InvokeAsync(CreateContext("/api/webhooks/stripe", "10.0.0.3"));

        Assert.Equal(10, nextCalled);
    }

    [Fact]
    public async Task DifferentIps_HaveSeparateBudgets()
    {
        var nextCalled = 0;
        var middleware = CreateMiddleware(new InMemoryRateLimiter(), _ =>
        {
            nextCalled++;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(CreateContext("/api/payments", "10.0.0.4"));
        await middleware.InvokeAsync(CreateContext("/api/payments", "10.0.0.4"));

        // First IP exhausted; a different IP still has its full budget
        await middleware.InvokeAsync(CreateContext("/api/payments", "10.0.0.5"));

        Assert.Equal(3, nextCalled);
    }

    [Fact]
    public async Task DisabledLimiter_EverythingPasses()
    {
        var options = new RateLimitOptions
        {
            Enabled = false,
            MaxRequests = TestOptions.MaxRequests,
            WindowSeconds = TestOptions.WindowSeconds,
            ApplyToPathPrefixes = TestOptions.ApplyToPathPrefixes
        };
        var nextCalled = 0;
        var middleware = new RateLimitingMiddleware(
            _ =>
            {
                nextCalled++;
                return Task.CompletedTask;
            },
            new InMemoryRateLimiter(),
            Options.Create(options));

        for (var i = 0; i < 5; i++)
            await middleware.InvokeAsync(CreateContext("/api/payments", "10.0.0.6"));

        Assert.Equal(5, nextCalled);
    }
}
