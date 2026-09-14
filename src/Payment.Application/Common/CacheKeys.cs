namespace Payment.Application.Common;

/// <summary>
/// Phase 11 — canonical cache key builders.
/// Keys are short-lived optimizations; SQL Server remains authoritative.
/// </summary>
public static class CacheKeys
{
    /// <summary>Cached payment read model (GET /api/payments/{id}).</summary>
    public static string PaymentStatus(Guid paymentId) => $"payment:status:{paymentId}";

    /// <summary>Fast-path marker: webhook event already processed successfully.</summary>
    public static string WebhookProcessed(string provider, string eventId) =>
        $"webhook:processed:{provider}:{eventId}";

    /// <summary>Serializes concurrent processing of webhooks for one payment.</summary>
    public static string WebhookPaymentLock(string providerPaymentId) =>
        $"webhook:payment:{providerPaymentId}";

    /// <summary>Serializes concurrent refund requests for one payment.</summary>
    public static string RefundLock(Guid paymentId) => $"refund:{paymentId}";
}
