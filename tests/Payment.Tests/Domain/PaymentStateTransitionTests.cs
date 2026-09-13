using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Domain.Exceptions;

namespace Payment.Tests.Domain;

public class PaymentStateTransitionTests
{
    private static Payment.Domain.Entities.Payment CreatePayment(decimal amount = 100m)
    {
        return new Payment.Domain.Entities.Payment(
            orderId: Guid.NewGuid(),
            provider: "Stripe",
            amount: amount,
            currency: "USD");
    }

    [Fact]
    public void NewPayment_ShouldHavePendingStatus()
    {
        var payment = CreatePayment();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void MarkSucceeded_FromPending_ShouldSucceed()
    {
        var payment = CreatePayment();

        payment.MarkSucceeded();

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void MarkFailed_FromPending_ShouldSucceed()
    {
        var payment = CreatePayment();

        payment.MarkFailed();

        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void Cancel_FromPending_ShouldSucceed()
    {
        var payment = CreatePayment();

        payment.Cancel();

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public void MarkProcessing_FromPending_ShouldSucceed()
    {
        var payment = CreatePayment();

        payment.MarkProcessing();

        Assert.Equal(PaymentStatus.Processing, payment.Status);
    }

    [Fact]
    public void MarkSucceeded_FromProcessing_ShouldSucceed()
    {
        var payment = CreatePayment();
        payment.MarkProcessing();

        payment.MarkSucceeded();

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void MarkFailed_FromProcessing_ShouldSucceed()
    {
        var payment = CreatePayment();
        payment.MarkProcessing();

        payment.MarkFailed();

        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void MarkRefunded_FromSucceeded_ShouldSucceed()
    {
        var payment = CreatePayment();
        payment.MarkSucceeded();

        payment.MarkRefunded();

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public void MarkSucceeded_FromSucceeded_ShouldThrow()
    {
        var payment = CreatePayment();
        payment.MarkSucceeded();

        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkSucceeded());
    }

    [Fact]
    public void MarkFailed_FromSucceeded_ShouldThrow()
    {
        var payment = CreatePayment();
        payment.MarkSucceeded();

        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkFailed());
    }

    [Fact]
    public void MarkRefunded_FromPending_ShouldThrow()
    {
        var payment = CreatePayment();

        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkRefunded());
    }

    [Fact]
    public void MarkRefunded_FromFailed_ShouldThrow()
    {
        var payment = CreatePayment();
        payment.MarkFailed();

        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkRefunded());
    }

    [Fact]
    public void Cancel_FromSucceeded_ShouldThrow()
    {
        var payment = CreatePayment();
        payment.MarkSucceeded();

        Assert.Throws<InvalidStateTransitionException>(() => payment.Cancel());
    }

    [Fact]
    public void MarkProcessing_FromSucceeded_ShouldThrow()
    {
        var payment = CreatePayment();
        payment.MarkSucceeded();

        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkProcessing());
    }

    [Fact]
    public void MarkProcessing_FromFailed_ShouldThrow()
    {
        var payment = CreatePayment();
        payment.MarkFailed();

        Assert.Throws<InvalidStateTransitionException>(() => payment.MarkProcessing());
    }

    [Fact]
    public void Constructor_WithZeroAmount_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Payment.Domain.Entities.Payment(Guid.NewGuid(), "Stripe", 0, "USD"));
    }

    [Fact]
    public void Constructor_WithNegativeAmount_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            new Payment.Domain.Entities.Payment(Guid.NewGuid(), "Stripe", -10, "USD"));
    }

    [Fact]
    public void SetProviderPaymentId_ShouldUpdateProviderId()
    {
        var payment = CreatePayment();
        var providerId = "pi_test_123";

        payment.SetProviderPaymentId(providerId);

        Assert.Equal(providerId, payment.ProviderPaymentId);
    }
}
