using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Payments;
using Stripe;

namespace Payment.Infrastructure.Payments;

/// <summary>
/// Decorator that adds retry-with-backoff for transient provider failures
/// (timeouts, 5xx, rate limits, network errors).
///
/// Phase 7 — Design for failure: the payment provider WILL time out and WILL
/// be unavailable sometimes. Retrying transient failures prevents avoidable
/// checkout failures while non-transient errors fail fast.
/// </summary>
public class ResilientPaymentProviderDecorator : IPaymentProvider
{
    private readonly IPaymentProvider _inner;
    private readonly ILogger<ResilientPaymentProviderDecorator> _logger;
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;

    public ResilientPaymentProviderDecorator(
        IPaymentProvider inner,
        ILogger<ResilientPaymentProviderDecorator> logger,
        int maxAttempts = 3,
        TimeSpan? baseDelay = null)
    {
        _inner = inner;
        _logger = logger;
        _maxAttempts = maxAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(250);
    }

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetryAsync(
            () => _inner.CreateCheckoutSessionAsync(request, cancellationToken),
            "CreateCheckoutSession",
            cancellationToken);
    }

    public PaymentWebhookEvent VerifyWebhook(string payload, string signature)
    {
        // No retry: signature verification is a local, deterministic operation.
        // If it fails it is a security/validation problem, not a transient one.
        return _inner.VerifyWebhook(payload, signature);
    }

    public Task<ProviderPaymentDetails> GetPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetryAsync(
            () => _inner.GetPaymentAsync(providerPaymentId, cancellationToken),
            "GetPayment",
            cancellationToken);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                attempt++;
                return await operation();
            }
            catch (Exception ex) when (attempt < _maxAttempts && IsTransient(ex))
            {
                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                _logger.LogWarning(ex,
                    "Transient payment provider failure in {Operation} (attempt {Attempt}/{MaxAttempts}). Retrying in {Delay}ms.",
                    operationName, attempt, _maxAttempts, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Classifies exceptions as transient (worth retrying) or permanent (fail fast).
    /// </summary>
    public static bool IsTransient(Exception ex)
    {
        return ex is TimeoutException
            or HttpRequestException
            or IOException
            or TaskCanceledException
            || (ex is StripeException stripe &&
                ((int)stripe.HttpStatusCode >= 500 || (int)stripe.HttpStatusCode == 429));
    }
}
