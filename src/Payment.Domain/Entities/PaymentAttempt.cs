using Payment.Domain.Common;
using Payment.Domain.Enums;

namespace Payment.Domain.Entities;

public class PaymentAttempt : BaseEntity
{
    public Guid PaymentId { get; private set; }
    public string Provider { get; private set; } = null!;
    public string? ProviderPaymentId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public int AttemptNumber { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    // Navigation properties
    public Payment Payment { get; private set; } = null!;

    private PaymentAttempt() { } // EF Core constructor

    public PaymentAttempt(Guid paymentId, string provider, decimal amount, string currency, int attemptNumber)
    {
        PaymentId = paymentId;
        Provider = provider;
        Amount = amount;
        Currency = currency;
        AttemptNumber = attemptNumber;
    }

    public void SetProviderPaymentId(string providerPaymentId)
    {
        ProviderPaymentId = providerPaymentId;
    }

    public void MarkProcessing()
    {
        Status = PaymentStatus.Processing;
    }

    public void MarkSucceeded()
    {
        Status = PaymentStatus.Succeeded;
        CompletedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string? failureReason = null)
    {
        Status = PaymentStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        FailureReason = failureReason;
    }
}
