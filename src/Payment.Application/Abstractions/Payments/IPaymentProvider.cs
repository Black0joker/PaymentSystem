namespace Payment.Application.Abstractions.Payments;

/// <summary>
/// Abstraction over external payment providers.
/// The Application layer never knows about Stripe-specific types.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>
    /// Creates a checkout session with the payment provider.
    /// Returns a URL the customer is redirected to.
    /// </summary>
    Task<CheckoutSessionResult> CreateCheckoutSessionAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the webhook signature and parses the event.
    /// Throws if the signature is invalid.
    /// </summary>
    PaymentWebhookEvent VerifyWebhook(string payload, string signature);

    /// <summary>
    /// Retrieves payment details from the provider.
    /// Used for reconciliation.
    /// </summary>
    Task<ProviderPaymentDetails> GetPaymentAsync(
        string providerPaymentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates a refund for a captured payment.
    /// The refund is NOT considered final until the provider confirms it
    /// via a refund webhook — this call only starts the flow.
    /// </summary>
    Task<RefundResult> CreateRefundAsync(
        RefundRequest request,
        CancellationToken cancellationToken = default);
}
