using Payment.Domain.Common;
using Payment.Domain.Enums;
using Payment.Domain.Exceptions;

namespace Payment.Domain.Entities;

public class Order : BaseEntity
{
    public Guid UserId { get; private set; }
    public string OrderNumber { get; private set; } = null!;
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;
    public string Currency { get; private set; } = "USD";
    public decimal Subtotal { get; private set; }
    public decimal Total { get; private set; }

    // Navigation properties
    public User User { get; private set; } = null!;
    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; private set; } = new List<Payment>();

    private Order() { } // EF Core constructor

    public Order(Guid userId, string currency = "USD")
    {
        UserId = userId;
        Currency = currency;
        OrderNumber = GenerateOrderNumber();
    }

    private static string GenerateOrderNumber()
    {
        return $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }

    public void AddItem(Course course, int quantity = 1)
    {
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));

        if (Status != OrderStatus.Pending)
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), "AddItem");

        var item = new OrderItem(Id, course.Id, course.Price, quantity);
        Items.Add(item);
        RecalculateTotals();
        Touch();
    }

    private void RecalculateTotals()
    {
        Subtotal = Items.Sum(i => i.TotalPrice);
        Total = Subtotal;
    }

    // State machine transitions
    public void MarkPaymentProcessing()
    {
        if (Status != OrderStatus.Pending)
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.PaymentProcessing.ToString());

        Status = OrderStatus.PaymentProcessing;
        Touch();
    }

    /// <summary>
    /// Compensation transition: returns the order to Pending when the payment provider
    /// call failed (e.g. timeout/unavailable) so the customer can retry checkout.
    /// Only allowed from PaymentProcessing — never from a terminal paid/failed state.
    /// </summary>
    public void ReturnToPending()
    {
        if (Status != OrderStatus.PaymentProcessing)
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.Pending.ToString());

        Status = OrderStatus.Pending;
        Touch();
    }

    public void MarkPaid()
    {
        if (Status != OrderStatus.PaymentProcessing)
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.Paid.ToString());

        Status = OrderStatus.Paid;
        Touch();
    }

    public void MarkFailed()
    {
        if (Status != OrderStatus.PaymentProcessing)
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.Failed.ToString());

        Status = OrderStatus.Failed;
        Touch();
    }

    public void Cancel()
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.PaymentProcessing))
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.Cancelled.ToString());

        Status = OrderStatus.Cancelled;
        Touch();
    }

    public void MarkExpired()
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.PaymentProcessing))
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.Expired.ToString());

        Status = OrderStatus.Expired;
        Touch();
    }

    /// <summary>
    /// Starts the refund flow: Paid -> RefundProcessing.
    /// </summary>
    public void StartRefund()
    {
        if (Status != OrderStatus.Paid)
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.RefundProcessing.ToString());

        Status = OrderStatus.RefundProcessing;
        Touch();
    }

    /// <summary>
    /// Confirms the refund once the provider webhook arrives.
    /// </summary>
    public void MarkRefunded()
    {
        if (Status != OrderStatus.RefundProcessing)
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.Refunded.ToString());

        Status = OrderStatus.Refunded;
        Touch();
    }

    /// <summary>
    /// Compensation transition: the provider refund call failed, so the order
    /// returns to Paid (the purchase is still valid).
    /// </summary>
    public void CancelRefund()
    {
        if (Status != OrderStatus.RefundProcessing)
            throw new InvalidStateTransitionException(nameof(Order), Status.ToString(), OrderStatus.Paid.ToString());

        Status = OrderStatus.Paid;
        Touch();
    }
}
