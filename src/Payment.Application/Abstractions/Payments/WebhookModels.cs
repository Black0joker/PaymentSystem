namespace Payment.Application.Abstractions.Payments;

/// <summary>
/// Represents a verified webhook event from the payment provider.
/// Maps provider-specific events to our domain concepts.
/// </summary>
public class PaymentWebhookEvent
{
    public string EventId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string? PaymentIntentId { get; set; }
    public string? SessionId { get; set; }

    /// <summary>Provider refund ID (set on refund webhooks, e.g. charge.refunded).</summary>
    public string? RefundId { get; set; }

    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? FailureReason { get; set; }
    public string RawPayload { get; set; } = null!;

    /// <summary>
    /// Maps the provider event type to our internal domain event type.
    /// </summary>
    public MappedEventType GetMappedEventType()
    {
        return EventType switch
        {
            "checkout.session.completed" => MappedEventType.CheckoutCompleted,
            "payment_intent.succeeded" => MappedEventType.PaymentSucceeded,
            "payment_intent.payment_failed" => MappedEventType.PaymentFailed,
            "charge.refunded" => MappedEventType.PaymentRefunded,
            _ => MappedEventType.Unknown
        };
    }
}

/// <summary>
/// Our domain-level event types, independent of any provider.
/// </summary>
public enum MappedEventType
{
    Unknown,
    CheckoutCompleted,
    PaymentSucceeded,
    PaymentFailed,
    PaymentRefunded
}
