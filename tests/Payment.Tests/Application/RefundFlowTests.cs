using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Application.Abstractions.Caching;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Features.Payments.Commands.RefundPayment;
using Payment.Application.Features.Webhooks.Commands.ProcessWebhook;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Domain.Events;
using Payment.Infrastructure.Persistence;

namespace Payment.Tests.Application;

/// <summary>
/// Phase 9 — refund flow tests.
///
/// Covers the full refund lifecycle:
///   API refund request -> RefundProcessing -> provider webhook -> Refunded
/// plus compensation when the provider call fails, out-of-order webhook
/// handling, and idempotency.
/// </summary>
public class RefundFlowTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<IPaymentProvider> _paymentProviderMock;
    private readonly RefundPaymentCommandHandler _refundHandler;
    private readonly ProcessWebhookCommandHandler _webhookHandler;

    public RefundFlowTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _paymentProviderMock = new Mock<IPaymentProvider>();

        _refundHandler = new RefundPaymentCommandHandler(
            _context,
            _paymentProviderMock.Object,
            Mock.Of<ILogger<RefundPaymentCommandHandler>>());

        _webhookHandler = new ProcessWebhookCommandHandler(
            _context,
            _paymentProviderMock.Object,
            Mock.Of<ILogger<ProcessWebhookCommandHandler>>());
    }

    public void Dispose() => _context.Dispose();

    /// <summary>
    /// Seeds a fully paid order + succeeded payment (the realistic starting
    /// point for any refund).
    /// </summary>
    private async Task<(Payment.Domain.Entities.Order Order, Payment.Domain.Entities.Payment Payment)> SeedPaidOrderAsync()
    {
        var order = new Payment.Domain.Entities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);

        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m, "USD");
        payment.SetProviderPaymentId("pi_test_refund");
        payment.MarkProcessing();
        payment.MarkSucceeded();
        _context.Payments.Add(payment);

        order.MarkPaid();

        await _context.SaveChangesAsync();
        return (order, payment);
    }

    // ---------- Refund initiation (RefundPaymentCommandHandler) ----------

    [Fact]
    public async Task RefundPayment_HappyPath_ShouldTransitionBothToRefundProcessingAndCreateRefund()
    {
        var (order, payment) = await SeedPaidOrderAsync();

        _paymentProviderMock
            .Setup(p => p.CreateRefundAsync(It.IsAny<RefundRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult { ProviderRefundId = "re_test_123", Status = "succeeded" });

        var result = await _refundHandler.Handle(
            new RefundPaymentCommand(payment.Id, "customer request"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("re_test_123", result.Value!.ProviderRefundId);

        // Payment and order are RefundProcessing — NOT yet Refunded (webhook confirms)
        Assert.Equal(PaymentStatus.RefundProcessing, payment.Status);
        Assert.Equal(OrderStatus.RefundProcessing, order.Status);

        // Refund record persisted with the provider refund id
        var refund = await _context.Refunds.SingleAsync(r => r.PaymentId == payment.Id);
        Assert.Equal(RefundStatus.Pending, refund.Status);
        Assert.Equal("re_test_123", refund.ProviderRefundId);
        Assert.Equal(100m, refund.Amount);
    }

    [Fact]
    public async Task RefundPayment_ProviderFailure_ShouldCompensateBackToOriginalStates()
    {
        var (order, payment) = await SeedPaidOrderAsync();

        _paymentProviderMock
            .Setup(p => p.CreateRefundAsync(It.IsAny<RefundRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("provider timeout"));

        var result = await _refundHandler.Handle(
            new RefundPaymentCommand(payment.Id, null), CancellationToken.None);

        Assert.False(result.IsSuccess);

        // Compensation: money is still captured, so states return to pre-refund
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);

        // The refund record is marked Failed with a reason
        var refund = await _context.Refunds.SingleAsync(r => r.PaymentId == payment.Id);
        Assert.Equal(RefundStatus.Failed, refund.Status);
        Assert.Contains("provider timeout", refund.FailureReason);
    }

    [Fact]
    public async Task RefundPayment_NonSucceededPayment_ShouldFailWithoutProviderCall()
    {
        var order = new Payment.Domain.Entities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);

        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m);
        payment.MarkProcessing(); // still processing
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        var result = await _refundHandler.Handle(
            new RefundPaymentCommand(payment.Id, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        _paymentProviderMock.Verify(
            p => p.CreateRefundAsync(It.IsAny<RefundRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefundPayment_MissingProviderPaymentId_ShouldFail()
    {
        var order = new Payment.Domain.Entities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);

        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m);
        payment.MarkProcessing();
        payment.MarkSucceeded(); // succeeded but no provider id (shouldn't happen, guard anyway)
        _context.Payments.Add(payment);
        order.MarkPaid();
        await _context.SaveChangesAsync();

        var result = await _refundHandler.Handle(
            new RefundPaymentCommand(payment.Id, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("provider payment ID", result.Error);
    }

    [Fact]
    public async Task RefundPayment_UnknownPayment_ShouldFail()
    {
        var result = await _refundHandler.Handle(
            new RefundPaymentCommand(Guid.NewGuid(), null), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    // ---------- Distributed lock contention (Busy vs Unavailable) ----------

    private sealed class StubDistributedLock : IDistributedLock
    {
        private readonly LockAcquireStatus _status;

        public StubDistributedLock(LockAcquireStatus status) => _status = status;

        public Task<LockAcquisitionResult> TryAcquireAsync(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            IAsyncDisposable? handle = _status == LockAcquireStatus.Acquired
                ? new NoOpHandle()
                : null;
            return Task.FromResult(new LockAcquisitionResult(_status, handle));
        }

        private sealed class NoOpHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task RefundPayment_LockBusy_ShouldFailFastWithoutProviderCall()
    {
        var (order, payment) = await SeedPaidOrderAsync();

        var handler = new RefundPaymentCommandHandler(
            _context,
            _paymentProviderMock.Object,
            Mock.Of<ILogger<RefundPaymentCommandHandler>>(),
            new StubDistributedLock(LockAcquireStatus.Busy),
            new NoOpCacheService());

        var result = await handler.Handle(
            new RefundPaymentCommand(payment.Id, null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("already in progress", result.Error);

        // No DB state changed, provider never called — no double refund.
        _paymentProviderMock.Verify(
            p => p.CreateRefundAsync(It.IsAny<RefundRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(_context.Refunds);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public async Task RefundPayment_LockUnavailable_ShouldProceedViaDbGuards()
    {
        var (order, payment) = await SeedPaidOrderAsync();

        _paymentProviderMock
            .Setup(p => p.CreateRefundAsync(It.IsAny<RefundRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefundResult { ProviderRefundId = "re_test_lockdown", Status = "succeeded" });

        var handler = new RefundPaymentCommandHandler(
            _context,
            _paymentProviderMock.Object,
            Mock.Of<ILogger<RefundPaymentCommandHandler>>(),
            new StubDistributedLock(LockAcquireStatus.Unavailable),
            new NoOpCacheService());

        var result = await handler.Handle(
            new RefundPaymentCommand(payment.Id, "customer request"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.RefundProcessing, payment.Status);
        Assert.Equal(OrderStatus.RefundProcessing, order.Status);
    }

    // ---------- Refund webhook confirmation (HandlePaymentRefundedAsync) ----------

    private async Task SendRefundWebhookAsync(string providerPaymentId, string eventId, string? refundId = null)
    {
        var payload = "{}";
        var signature = "sig";

        _paymentProviderMock
            .Setup(p => p.VerifyWebhook(payload, signature))
            .Returns(new PaymentWebhookEvent
            {
                EventId = eventId,
                EventType = "charge.refunded",
                Provider = "Stripe",
                PaymentIntentId = providerPaymentId,
                RefundId = refundId,
                Amount = 100m,
                Currency = "USD",
                RawPayload = payload
            });

        await _webhookHandler.Handle(new ProcessWebhookCommand(payload, signature), CancellationToken.None);
    }

    [Fact]
    public async Task RefundWebhook_FromRefundProcessing_ShouldConfirmRefundAndPublishOutboxEvent()
    {
        var (order, payment) = await SeedPaidOrderAsync();

        // Simulate our refund flow having started
        payment.StartRefund();
        order.StartRefund();
        var refund = new Refund(payment.Id, order.Id, 100m, "USD");
        refund.SetProviderRefundId("re_test_999");
        _context.Refunds.Add(refund);
        await _context.SaveChangesAsync();

        await SendRefundWebhookAsync("pi_test_refund", "evt_refund_1", "re_test_999");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(RefundStatus.Succeeded, refund.Status);

        // OrderRefundedEvent published via outbox in the same transaction
        var outboxEvent = await _context.OutboxMessages
            .AnyAsync(m => m.EventType == nameof(OrderRefundedEvent));
        Assert.True(outboxEvent);
    }

    [Fact]
    public async Task RefundWebhook_DashboardRefund_FromSucceeded_ShouldConfirmWithoutRefundRecord()
    {
        var (order, payment) = await SeedPaidOrderAsync();

        // Refund issued outside our system: payment still Succeeded, order still Paid
        await SendRefundWebhookAsync("pi_test_refund", "evt_refund_dashboard");

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(OrderStatus.Refunded, order.Status);
    }

    [Fact]
    public async Task RefundWebhook_Duplicate_ShouldBeIdempotentNoOp()
    {
        var (order, payment) = await SeedPaidOrderAsync();
        payment.StartRefund();
        order.StartRefund();
        await _context.SaveChangesAsync();

        await SendRefundWebhookAsync("pi_test_refund", "evt_refund_dup_1");
        var firstOutboxCount = await _context.OutboxMessages.CountAsync();

        // Provider retries the same webhook
        var result = await _webhookHandler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        // No duplicate outbox event
        Assert.Equal(firstOutboxCount, await _context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task RefundWebhook_OutOfOrder_PaymentStillPending_ShouldRecordFailure()
    {
        var order = new Payment.Domain.Entities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);

        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m);
        payment.SetProviderPaymentId("pi_test_ooo");
        _context.Payments.Add(payment); // still Pending
        await _context.SaveChangesAsync();

        await SendRefundWebhookAsync("pi_test_ooo", "evt_refund_ooo");

        // Nothing changed — the refund is NOT applied to a Pending payment
        Assert.Equal(PaymentStatus.Pending, payment.Status);

        // The webhook event is recorded as Failed so the provider's retry can reprocess it
        var webhookEvent = await _context.WebhookEvents
            .SingleAsync(e => e.ProviderEventId == "evt_refund_ooo");
        Assert.Equal(WebhookEventStatus.Failed, webhookEvent.Status);
    }

    // ---------- Real-flow fix: session ID -> payment intent ID upgrade ----------

    [Fact]
    public async Task CheckoutCompletedWebhook_ShouldUpgradeProviderPaymentIdToPaymentIntent()
    {
        var order = new Payment.Domain.Entities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);

        // Checkout stores the SESSION ID as provider payment id
        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m);
        payment.SetProviderPaymentId("cs_session_123");
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        var payload = "{}";
        var signature = "sig";

        _paymentProviderMock
            .Setup(p => p.VerifyWebhook(payload, signature))
            .Returns(new PaymentWebhookEvent
            {
                EventId = "evt_checkout_1",
                EventType = "checkout.session.completed",
                Provider = "Stripe",
                SessionId = "cs_session_123",
                PaymentIntentId = "pi_real_intent",
                Status = "succeeded",
                Amount = 100m,
                Currency = "USD",
                RawPayload = payload
            });

        var result = await _webhookHandler.Handle(
            new ProcessWebhookCommand(payload, signature), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);

        // ProviderPaymentId upgraded from session id to payment intent id so
        // payment_intent.* webhooks and reconciliation can locate the payment.
        Assert.Equal("pi_real_intent", payment.ProviderPaymentId);
    }
}
