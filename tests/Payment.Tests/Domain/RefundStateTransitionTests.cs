using Payment.Domain.Enums;
using Payment.Domain.Exceptions;
using Payment.Domain.Entities;

namespace Payment.Tests.Domain;

/// <summary>
/// Phase 9 — refund state machine tests.
///
/// Invariant: a payment is only Refunded once the provider confirms the refund
/// (webhook). Between request and confirmation it is RefundProcessing.
/// </summary>
public class RefundStateTransitionTests
{
    private static Payment.Domain.Entities.Payment CreateSucceededPayment()
    {
        var payment = new Payment.Domain.Entities.Payment(Guid.NewGuid(), "Stripe", 100m, "USD");
        payment.MarkProcessing();
        payment.MarkSucceeded();
        return payment;
    }

    private static Order CreatePaidOrder()
    {
        var order = new Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        order.MarkPaid();
        return order;
    }

    // ---------- Payment refund transitions ----------

    [Fact]
    public void StartRefund_FromSucceeded_ShouldTransitionToRefundProcessing()
    {
        var payment = CreateSucceededPayment();

        payment.StartRefund();

        Assert.Equal(PaymentStatus.RefundProcessing, payment.Status);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Refunded)]
    [InlineData(PaymentStatus.RefundProcessing)]
    public void StartRefund_FromNonSucceededState_ShouldThrow(PaymentStatus initial)
    {
        var payment = new Payment.Domain.Entities.Payment(Guid.NewGuid(), "Stripe", 100m);

        // Drive the payment into the desired initial state via legal transitions
        switch (initial)
        {
            case PaymentStatus.Processing:
                payment.MarkProcessing();
                break;
            case PaymentStatus.Failed:
                payment.MarkProcessing();
                payment.MarkFailed();
                break;
            case PaymentStatus.Cancelled:
                payment.Cancel();
                break;
            case PaymentStatus.Refunded:
                payment.MarkProcessing();
                payment.MarkSucceeded();
                payment.MarkRefunded(); // dashboard-style refund: allowed from Succeeded
                break;
            case PaymentStatus.RefundProcessing:
                payment.MarkProcessing();
                payment.MarkSucceeded();
                payment.StartRefund();
                break;
        }

        Assert.Equal(initial, payment.Status);
        Assert.Throws<InvalidStateTransitionException>(() => payment.StartRefund());
    }

    [Fact]
    public void CancelRefund_FromRefundProcessing_ShouldReturnToSucceeded()
    {
        var payment = CreateSucceededPayment();
        payment.StartRefund();

        payment.CancelRefund();

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void CancelRefund_FromNonRefundProcessingState_ShouldThrow()
    {
        var payment = CreateSucceededPayment();

        Assert.Throws<InvalidStateTransitionException>(() => payment.CancelRefund());
    }

    [Fact]
    public void MarkRefunded_FromRefundProcessing_ShouldTransitionToRefunded()
    {
        var payment = CreateSucceededPayment();
        payment.StartRefund();

        payment.MarkRefunded();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void MarkRefunded_FromSucceeded_ShouldTransitionToRefunded()
    {
        // Covers refunds issued outside our system (e.g. provider dashboard)
        var payment = CreateSucceededPayment();

        payment.MarkRefunded();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Processing)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    [InlineData(PaymentStatus.Refunded)]
    public void MarkRefunded_FromInvalidState_ShouldThrow(PaymentStatus initial)
    {
        var payment = new Payment.Domain.Entities.Payment(Guid.NewGuid(), "Stripe", 100m);

        switch (initial)
        {
            case PaymentStatus.Processing:
                payment.MarkProcessing();
                break;
            case PaymentStatus.Failed:
                payment.MarkProcessing();
                payment.MarkFailed();
                break;
            case PaymentStatus.Cancelled:
                payment.Cancel();
                break;
            case PaymentStatus.Refunded:
                payment.MarkProcessing();
                payment.MarkSucceeded();
                payment.MarkRefunded();
                break;
        }

        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkRefunded());
    }

    // ---------- Order refund transitions ----------

    [Fact]
    public void Order_StartRefund_FromPaid_ShouldTransitionToRefundProcessing()
    {
        var order = CreatePaidOrder();

        order.StartRefund();

        Assert.Equal(OrderStatus.RefundProcessing, order.Status);
    }

    [Fact]
    public void Order_StartRefund_FromNonPaidState_ShouldThrow()
    {
        var order = new Order(Guid.NewGuid());

        Assert.Throws<InvalidStateTransitionException>(() => order.StartRefund());
    }

    [Fact]
    public void Order_MarkRefunded_FromRefundProcessing_ShouldTransitionToRefunded()
    {
        var order = CreatePaidOrder();
        order.StartRefund();

        order.MarkRefunded();

        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public void Order_MarkRefunded_FromNonRefundProcessingState_ShouldThrow()
    {
        var order = CreatePaidOrder();

        Assert.Throws<InvalidStateTransitionException>(() => order.MarkRefunded());
    }

    [Fact]
    public void Order_CancelRefund_FromRefundProcessing_ShouldReturnToPaid()
    {
        var order = CreatePaidOrder();
        order.StartRefund();

        order.CancelRefund();

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void Order_CancelRefund_FromNonRefundProcessingState_ShouldThrow()
    {
        var order = CreatePaidOrder();

        Assert.Throws<InvalidStateTransitionException>(() => order.CancelRefund());
    }

    // ---------- Refund entity ----------

    [Fact]
    public void Refund_Constructor_ShouldDefaultToPending()
    {
        var refund = new Refund(Guid.NewGuid(), Guid.NewGuid(), 100m, "USD", "customer request");

        Assert.Equal(RefundStatus.Pending, refund.Status);
        Assert.Null(refund.CompletedAt);
    }

    [Fact]
    public void Refund_Constructor_WithNonPositiveAmount_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Refund(Guid.NewGuid(), Guid.NewGuid(), 0m, "USD"));
    }

    [Fact]
    public void Refund_MarkSucceeded_ShouldSetStatusAndCompletedAt()
    {
        var refund = new Refund(Guid.NewGuid(), Guid.NewGuid(), 100m, "USD");

        refund.MarkSucceeded();

        Assert.Equal(RefundStatus.Succeeded, refund.Status);
        Assert.NotNull(refund.CompletedAt);
    }

    [Fact]
    public void Refund_MarkFailed_ShouldSetStatusReasonAndCompletedAt()
    {
        var refund = new Refund(Guid.NewGuid(), Guid.NewGuid(), 100m, "USD");

        refund.MarkFailed("provider timeout");

        Assert.Equal(RefundStatus.Failed, refund.Status);
        Assert.Equal("provider timeout", refund.FailureReason);
        Assert.NotNull(refund.CompletedAt);
    }
}
