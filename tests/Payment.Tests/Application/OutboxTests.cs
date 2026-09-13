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
/// Phase 8 — Outbox write-side tests: domain events must be written to the
/// outbox in the SAME transactional unit as the state change.
/// </summary>
public class OutboxTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<IPaymentProvider> _providerMock;
    private readonly ProcessWebhookCommandHandler _handler;

    public OutboxTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _providerMock = new Mock<IPaymentProvider>();
        _handler = new ProcessWebhookCommandHandler(
            _context, _providerMock.Object, Mock.Of<ILogger<ProcessWebhookCommandHandler>>());
    }

    public void Dispose() => _context.Dispose();

    private async Task SeedPaymentAsync(string providerId)
    {
        var order = new Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);

        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m, "USD");
        payment.SetProviderPaymentId(providerId);
        _context.Payments.Add(payment);

        await _context.SaveChangesAsync();
    }

    private void SetupVerifiedEvent(string eventId, string providerId, string eventType, string? failureReason = null)
    {
        _providerMock
            .Setup(p => p.VerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new PaymentWebhookEvent
            {
                EventId = eventId,
                EventType = eventType,
                Provider = "Stripe",
                PaymentIntentId = providerId,
                Status = eventType,
                FailureReason = failureReason,
                RawPayload = "{}"
            });
    }

    [Fact]
    public async Task PaymentSucceeded_WritesPaymentSucceededAndOrderPaidOutboxMessages()
    {
        await SeedPaymentAsync("pi_outbox_1");
        SetupVerifiedEvent("evt_ob_1", "pi_outbox_1", "payment_intent.succeeded");

        var result = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var messages = await _context.OutboxMessages.ToListAsync();
        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, m => m.EventType == "PaymentSucceededEvent");
        Assert.Contains(messages, m => m.EventType == "OrderPaidEvent");

        // All messages start Pending with valid payloads
        Assert.All(messages, m =>
        {
            Assert.Equal(OutboxMessageStatus.Pending, m.Status);
            Assert.False(string.IsNullOrWhiteSpace(m.Payload));
        });
    }

    [Fact]
    public async Task PaymentFailed_WritesPaymentFailedOutboxMessage()
    {
        await SeedPaymentAsync("pi_outbox_2");
        SetupVerifiedEvent("evt_ob_2", "pi_outbox_2", "payment_intent.payment_failed", "card_declined");

        var result = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);
        Assert.True(result.IsSuccess);

        var message = await _context.OutboxMessages.SingleAsync();
        Assert.Equal("PaymentFailedEvent", message.EventType);
        Assert.Contains("card_declined", message.Payload);
    }

    [Fact]
    public async Task FailedProcessing_WritesNoOutboxMessages()
    {
        // Payment not found -> processing fails -> no events may be published
        SetupVerifiedEvent("evt_ob_3", "pi_missing", "payment_intent.succeeded");

        var result = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);
        Assert.False(result.IsSuccess);

        Assert.Empty(await _context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task DuplicateSucceededWebhook_DoesNotWriteDuplicateOutboxMessages()
    {
        await SeedPaymentAsync("pi_outbox_4");
        SetupVerifiedEvent("evt_ob_4", "pi_outbox_4", "payment_intent.succeeded");

        await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);
        await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None); // duplicate

        // Exactly one OrderPaid event despite two deliveries
        var orderPaidCount = await _context.OutboxMessages
            .CountAsync(m => m.EventType == "OrderPaidEvent");
        Assert.Equal(1, orderPaidCount);
    }
}
