using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Payments;

namespace Payment.Infrastructure.Payments.Fake;

/// <summary>
/// Fake payment provider for development and testing.
/// Simulates successful checkout sessions and webhooks without hitting a real provider.
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
        var sessionId = $"fake_session_{Guid.NewGuid():N}";
        var checkoutUrl = $"https://localhost:5001/fake-checkout/{sessionId}";

        _logger.LogInformation(
            "Fake checkout session created. SessionId={SessionId}, OrderId={OrderId}",
            sessionId, request.OrderId);

        return Task.FromResult(new CheckoutSessionResult
        {
            SessionId = sessionId,
            CheckoutUrl = checkoutUrl,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        });
    }

    public PaymentWebhookEvent VerifyWebhook(string payload, string signature)
    {
        // In fake mode, we accept any webhook without signature verification
        // This is ONLY for development/testing
        _logger.LogInformation("Fake webhook received. Payload={Payload}", payload);

        // Parse the simple JSON payload
        var eventId = $"fake_evt_{Guid.NewGuid():N}";

        return new PaymentWebhookEvent
        {
            EventId = eventId,
            EventType = "payment_intent.succeeded",
            Provider = "Fake",
            PaymentIntentId = $"fake_pi_{Guid.NewGuid():N}",
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
}
