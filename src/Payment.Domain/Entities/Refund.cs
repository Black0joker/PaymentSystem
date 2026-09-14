using Payment.Domain.Common;
using Payment.Domain.Enums;

namespace Payment.Domain.Entities;

/// <summary>
/// A refund request against a succeeded payment.
/// Lifecycle: Pending (requested, provider call in flight or awaiting webhook)
/// -> Succeeded (provider confirmed via webhook) or Failed (provider call failed).
/// </summary>
public class Refund : BaseEntity
{
    public Guid PaymentId { get; private set; }
    public Guid OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public RefundStatus Status { get; private set; } = RefundStatus.Pending;
    public string? ProviderRefundId { get; private set; }
    public string? Reason { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    // Navigation properties
    public Payment Payment { get; private set; } = null!;

    private Refund() { } // EF Core constructor

    public Refund(Guid paymentId, Guid orderId, decimal amount, string currency, string? reason = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Refund amount must be greater than zero.", nameof(amount));

        PaymentId = paymentId;
        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        Reason = reason;
    }

    public void SetProviderRefundId(string providerRefundId)
    {
        ProviderRefundId = providerRefundId;
        Touch();
    }

    public void MarkSucceeded()
    {
        Status = RefundStatus.Succeeded;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void MarkFailed(string failureReason)
    {
        Status = RefundStatus.Failed;
        CompletedAt = DateTime.UtcNow;
        FailureReason = failureReason;
        Touch();
    }
}
