namespace Payment.Application.Abstractions.Payments;

/// <summary>
/// Payment details retrieved from the provider for reconciliation.
/// </summary>
public class ProviderPaymentDetails
{
    public string ProviderPaymentId { get; set; } = null!;
    public string Status { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public DateTime? CompletedAt { get; set; }
}
