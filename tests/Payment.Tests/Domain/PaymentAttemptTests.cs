using Payment.Domain.Entities;
using Payment.Domain.Enums;

namespace Payment.Tests.Domain;

public class PaymentAttemptTests
{
    [Fact]
    public void NewAttempt_ShouldHavePendingStatus()
    {
        var paymentId = Guid.NewGuid();
        var attempt = new PaymentAttempt(paymentId, "Stripe", 100m, "USD", attemptNumber: 1);

        Assert.Equal(PaymentStatus.Pending, attempt.Status);
        Assert.Equal(1, attempt.AttemptNumber);
    }

    [Fact]
    public void MarkProcessing_ShouldSetProcessingStatus()
    {
        var attempt = new PaymentAttempt(Guid.NewGuid(), "Stripe", 100m, "USD", 1);

        attempt.MarkProcessing();

        Assert.Equal(PaymentStatus.Processing, attempt.Status);
    }

    [Fact]
    public void MarkSucceeded_ShouldSetCompletedAt()
    {
        var attempt = new PaymentAttempt(Guid.NewGuid(), "Stripe", 100m, "USD", 1);

        attempt.MarkSucceeded();

        Assert.Equal(PaymentStatus.Succeeded, attempt.Status);
        Assert.NotNull(attempt.CompletedAt);
    }

    [Fact]
    public void MarkFailed_ShouldSetFailureReasonAndCompletedAt()
    {
        var attempt = new PaymentAttempt(Guid.NewGuid(), "Stripe", 100m, "USD", 1);
        var reason = "Insufficient funds";

        attempt.MarkFailed(reason);

        Assert.Equal(PaymentStatus.Failed, attempt.Status);
        Assert.Equal(reason, attempt.FailureReason);
        Assert.NotNull(attempt.CompletedAt);
    }

    [Fact]
    public void SetProviderPaymentId_ShouldUpdateId()
    {
        var attempt = new PaymentAttempt(Guid.NewGuid(), "Stripe", 100m, "USD", 1);
        var providerId = "pi_test_456";

        attempt.SetProviderPaymentId(providerId);

        Assert.Equal(providerId, attempt.ProviderPaymentId);
    }
}
