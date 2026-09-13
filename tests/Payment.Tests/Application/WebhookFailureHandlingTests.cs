using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Features.Webhooks.Commands.ProcessWebhook;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Persistence;

namespace Payment.Tests.Application;

/// <summary>
/// Phase 7 — Failure handling for webhooks:
///  - processing failures must not corrupt Payment/Order state
///  - a failed event must be reprocess-able when the provider retries
///  - out-of-order events (failed AFTER succeeded) are handled gracefully
///  - a crash during the final commit leaves no partial state; retry recovers
/// </summary>
public class WebhookFailureHandlingTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly AppDbContext _context;
    private readonly Mock<IPaymentProvider> _providerMock;
    private readonly ProcessWebhookCommandHandler _handler;

    public WebhookFailureHandlingTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: _dbName)
            .Options;

        _context = new AppDbContext(options);
        _providerMock = new Mock<IPaymentProvider>();
        _handler = new ProcessWebhookCommandHandler(
            _context, _providerMock.Object, Mock.Of<ILogger<ProcessWebhookCommandHandler>>());
    }

    public void Dispose() => _context.Dispose();

    private async Task<(Payment.Domain.Entities.Payment payment, Order order)> SeedPaymentAsync(string providerId)
    {
        var order = new Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);

        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m, "USD");
        payment.SetProviderPaymentId(providerId);
        _context.Payments.Add(payment);

        await _context.SaveChangesAsync();
        return (payment, order);
    }

    private void SetupVerifiedEvent(string eventId, string providerId, string eventType = "payment_intent.succeeded")
    {
        _providerMock
            .Setup(p => p.VerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new PaymentWebhookEvent
            {
                EventId = eventId,
                EventType = eventType,
                Provider = "Stripe",
                PaymentIntentId = providerId,
                Status = "succeeded",
                RawPayload = "{}"
            });
    }

    [Fact]
    public async Task ProcessingFailure_DoesNotCorruptState_RecordsFailedEvent()
    {
        // Payment exists but webhook references a DIFFERENT provider ID -> throws inside processing
        var (payment, order) = await SeedPaymentAsync("pi_local");
        SetupVerifiedEvent("evt_fail_1", "pi_unknown_provider");

        var result = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.False(result.IsSuccess);

        // Domain state untouched
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(OrderStatus.PaymentProcessing, order.Status);

        // Event recorded as Failed so the provider's retry can reprocess it
        var evt = await _context.WebhookEvents.SingleAsync();
        Assert.Equal(WebhookEventStatus.Failed, evt.Status);
        Assert.NotNull(evt.Error);
    }

    [Fact]
    public async Task RetriedFailedWebhook_ReprocessesSuccessfully()
    {
        var (payment, order) = await SeedPaymentAsync("pi_retry");

        // First attempt fails: payment not found for the webhook's provider ID
        SetupVerifiedEvent("evt_retry_1", "pi_unknown");
        var first = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);
        Assert.False(first.IsSuccess);

        var evt = await _context.WebhookEvents.SingleAsync();
        Assert.Equal(WebhookEventStatus.Failed, evt.Status);

        // Provider retries the SAME event — this time it maps to our payment
        SetupVerifiedEvent("evt_retry_1", "pi_retry");
        var second = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);

        // Still exactly one event row, now Processed
        Assert.Equal(1, await _context.WebhookEvents.CountAsync());
        Assert.Equal(WebhookEventStatus.Processed, evt.Status);
        Assert.Null(evt.Error);
    }

    [Fact]
    public async Task OutOfOrder_FailedAfterSucceeded_StateStaysSucceeded()
    {
        var (payment, _) = await SeedPaymentAsync("pi_ooo");

        // 1. succeeded webhook arrives and is processed
        SetupVerifiedEvent("evt_succeeded", "pi_ooo", "payment_intent.succeeded");
        var first = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);

        // 2. stale 'failed' webhook arrives afterwards (out of order)
        _providerMock
            .Setup(p => p.VerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new PaymentWebhookEvent
            {
                EventId = "evt_failed_late",
                EventType = "payment_intent.payment_failed",
                Provider = "Stripe",
                PaymentIntentId = "pi_ooo",
                Status = "failed",
                FailureReason = "stale event",
                RawPayload = "{}"
            });

        var second = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);

        // No-op success — terminal state wins, nothing corrupted
        Assert.True(second.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    /// <summary>
    /// DbContext that crashes on the Nth SaveChangesAsync call — simulates an
    /// application crash / failed commit during the final transaction commit.
    /// </summary>
    private class CrashingDbContext : AppDbContext
    {
        private int _saveCount;
        public int FailOnSaveNumber { get; init; } = 2;

        public CrashingDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _saveCount++;
            if (_saveCount == FailOnSaveNumber)
                throw new InvalidOperationException("Simulated crash during commit");
            return base.SaveChangesAsync(cancellationToken);
        }
    }

    [Fact]
    public async Task SimulatedCrash_DuringCommit_NoPartialState_RetryRecovers()
    {
        // Arrange: seed payment via the normal context (shared in-memory store)
        var (payment, order) = await SeedPaymentAsync("pi_crash");
        SetupVerifiedEvent("evt_crash", "pi_crash");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        await using var crashingContext = new CrashingDbContext(options) { FailOnSaveNumber = 2 };
        var crashingHandler = new ProcessWebhookCommandHandler(
            crashingContext, _providerMock.Object, Mock.Of<ILogger<ProcessWebhookCommandHandler>>());

        // Act 1: first webhook delivery crashes on the final commit
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            crashingHandler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None));

        // Assert 1: nothing was partially committed — payment/order unchanged
        var freshPayment = await _context.Payments.AsNoTracking().SingleAsync();
        var freshOrder = await _context.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentStatus.Pending, freshPayment.Status);
        Assert.Equal(OrderStatus.PaymentProcessing, freshOrder.Status);

        // The webhook event row exists as Received (inserted before the crash)
        var evt = await _context.WebhookEvents.AsNoTracking().SingleAsync();
        Assert.Equal(WebhookEventStatus.Received, evt.Status);

        // Act 2: the provider retries the webhook — this time the commit succeeds
        // Use a fresh DbContext to avoid any tracking issues from the crashed context
        await using var retryContext = new AppDbContext(options);
        var retryHandler = new ProcessWebhookCommandHandler(
            retryContext, _providerMock.Object, Mock.Of<ILogger<ProcessWebhookCommandHandler>>());

        var retry = await retryHandler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);

        // Assert 2: correct final state reached (reload from fresh context)
        Assert.True(retry.IsSuccess);
        var finalPayment = await retryContext.Payments.AsNoTracking().SingleAsync();
        var finalOrder = await retryContext.Orders.AsNoTracking().SingleAsync();
        Assert.Equal(PaymentStatus.Succeeded, finalPayment.Status);
        Assert.Equal(OrderStatus.Paid, finalOrder.Status);

        // Exactly one webhook event row, now Processed
        var finalEvent = await retryContext.WebhookEvents.AsNoTracking().SingleAsync();
        Assert.Equal(WebhookEventStatus.Processed, finalEvent.Status);
    }
}
