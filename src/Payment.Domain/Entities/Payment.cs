using Payment.Domain.Common;
using Payment.Domain.Enums;
using Payment.Domain.Exceptions;

namespace Payment.Domain.Entities;

public class Payment : BaseEntity
{
    public Guid OrderId { get; private set; }
    public string Provider { get; private set; } = null!;
    public string? ProviderPaymentId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;

    // Navigation properties
    public Order Order { get; private set; } = null!;
    public ICollection<PaymentAttempt> Attempts { get; private set; } = new List<PaymentAttempt>();

    private Payment() { } // EF Core constructor

    public Payment(Guid orderId, string provider, decimal amount, string currency = "USD")
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.", nameof(amount));

        OrderId = orderId;
        Provider = provider;
        Amount = amount;
        Currency = currency;
    }

    public void SetProviderPaymentId(string providerPaymentId)
    {
        ProviderPaymentId = providerPaymentId;
        Touch();
    }

    // State machine transitions
    public void MarkProcessing()
    {
        if (Status != PaymentStatus.Pending)
            throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), PaymentStatus.Processing.ToString());

        Status = PaymentStatus.Processing;
        Touch();
    }

    public void MarkSucceeded()
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), PaymentStatus.Succeeded.ToString());

        Status = PaymentStatus.Succeeded;
        Touch();
    }

    public void MarkFailed()
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), PaymentStatus.Failed.ToString());

        Status = PaymentStatus.Failed;
        Touch();
    }

    public void Cancel()
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), PaymentStatus.Cancelled.ToString());

        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void MarkRefunded()
    {
        if (Status != PaymentStatus.Succeeded)
            throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), PaymentStatus.Refunded.ToString());

        Status = PaymentStatus.Refunded;
        Touch();
    }
}
