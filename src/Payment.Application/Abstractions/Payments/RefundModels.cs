namespace Payment.Application.Abstractions.Payments;

/// <summary>
/// Request to initiate a refund with the payment provider.
/// </summary>
public class RefundRequest
{
    public string ProviderPaymentId { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Reason { get; set; }
}

/// <summary>
/// Result of initiating a refund. Status reflects the provider's immediate
/// response; final confirmation arrives via the refund webhook.
/// </summary>
public class RefundResult
{
    public string ProviderRefundId { get; set; } = null!;
    public string Status { get; set; } = null!;
}
