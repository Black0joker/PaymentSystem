using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Payments;

namespace Payment.Infrastructure.Payments.Fake;

/// <summary>
/// Simulates payment provider behavior for local development and testing.
/// Selected when PaymentProvider setting is "Fake".
/// </summary>
public class FakePaymentProvider : IPaymentProvider
{
    private readonly ILogger<FakePaymentProvider> _logger;

    public FakePaymentProvider(ILogger<FakePaymentProvider> logger)
    {
        _logger = logger;
    }

    public Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CheckoutSessionResult
        {
            SessionId = $"cs_fake_{Guid.NewGuid():N}",
            CheckoutUrl = $"https://fake-checkout.local/{request.OrderId}",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        });
    }

    public PaymentWebhookEvent VerifyWebhook(string payload, string signature)
    {
        return new PaymentWebhookEvent
        {
            EventId = $"evt_fake_{Guid.NewGuid():N}",
            EventType = "payment_intent.succeeded",
            Provider = "Fake",
            Status = "succeeded",
            RawPayload = payload
        };
    }

    public Task<ProviderPaymentDetails> GetPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ProviderPaymentDetails
        {
            ProviderPaymentId = providerPaymentId,
            Status = "succeeded",
            Amount = 0,
            Currency = "USD",
            CompletedAt = DateTime.UtcNow
        });
    }

    public Task<RefundResult> CreateRefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RefundResult
        {
            ProviderRefundId = $"re_fake_{Guid.NewGuid():N}",
            Status = "succeeded"
        });
    }
}
