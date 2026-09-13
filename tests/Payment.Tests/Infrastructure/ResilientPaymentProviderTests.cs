using Microsoft.Extensions.Logging;
using Moq;
using Payment.Application.Abstractions.Payments;
using Payment.Infrastructure.Payments;

namespace Payment.Tests.Infrastructure;

/// <summary>
/// Phase 7 — Provider resilience decorator tests:
/// transient failures are retried with backoff; permanent failures fail fast.
/// </summary>
public class ResilientPaymentProviderTests
{
    private readonly Mock<IPaymentProvider> _innerMock = new();
    private readonly ResilientPaymentProviderDecorator _decorator;

    public ResilientPaymentProviderTests()
    {
        _decorator = new ResilientPaymentProviderDecorator(
            _innerMock.Object,
            Mock.Of<ILogger<ResilientPaymentProviderDecorator>>(),
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(1)); // fast retries for tests
    }

    private static CheckoutRequest AnyRequest() => new()
    {
        OrderId = "order-1",
        OrderNumber = "ORD-1",
        Amount = 100m,
        Currency = "USD",
        SuccessUrl = "https://s",
        CancelUrl = "https://c",
        LineItems = new List<CheckoutLineItem>()
    };

    private static CheckoutSessionResult AnySession() => new()
    {
        SessionId = "cs_1",
        CheckoutUrl = "https://checkout",
        ExpiresAt = DateTime.UtcNow.AddMinutes(30)
    };

    [Fact]
    public async Task TransientFailureThenSuccess_ReturnsResult()
    {
        _innerMock
            .SetupSequence(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException())
            .ThrowsAsync(new HttpRequestException())
            .ReturnsAsync(AnySession());

        var result = await _decorator.CreateCheckoutSessionAsync(AnyRequest());

        Assert.Equal("cs_1", result.SessionId);
        _innerMock.Verify(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task AllAttemptsFail_ThrowsAfterMaxAttempts()
    {
        _innerMock
            .Setup(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("still down"));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            _decorator.CreateCheckoutSessionAsync(AnyRequest()));

        _innerMock.Verify(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task NonTransientFailure_FailsFast_NoRetry()
    {
        _innerMock
            .Setup(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("invalid request"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _decorator.CreateCheckoutSessionAsync(AnyRequest()));

        // Permanent errors are NOT retried
        _innerMock.Verify(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPayment_TransientFailureIsRetried()
    {
        _innerMock
            .SetupSequence(p => p.GetPaymentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503"))
            .ReturnsAsync(new ProviderPaymentDetails
            {
                ProviderPaymentId = "pi_1",
                Status = "succeeded",
                Amount = 100m,
                Currency = "USD"
            });

        var result = await _decorator.GetPaymentAsync("pi_1");

        Assert.Equal("succeeded", result.Status);
        _innerMock.Verify(p => p.GetPaymentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public void VerifyWebhook_IsNeverRetried()
    {
        _innerMock
            .Setup(p => p.VerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new WebhookVerificationException("bad signature"));

        Assert.Throws<WebhookVerificationException>(() => _decorator.VerifyWebhook("{}", "sig"));

        // Signature verification failures are security issues, not transient errors
        _innerMock.Verify(p => p.VerifyWebhook(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(HttpRequestException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(InvalidOperationException), false)]
    public void IsTransient_ClassifiesExceptions(Type exceptionType, bool expected)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "test")!;
        Assert.Equal(expected, ResilientPaymentProviderDecorator.IsTransient(exception));
    }
}
