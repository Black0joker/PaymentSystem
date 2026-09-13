using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Features.Webhooks.Commands.ProcessWebhook;
using Payment.Domain.Enums;
using Payment.Infrastructure.Persistence;
using Payment.Domain.Entities;

namespace Payment.Tests.Application;

/// <summary>
/// Tests webhook idempotency — the same webhook event should only be processed once.
/// Uses in-memory database for isolation.
/// </summary>
public class WebhookIdempotencyTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<IPaymentProvider> _paymentProviderMock;
    private readonly ProcessWebhookCommandHandler _handler;

    public WebhookIdempotencyTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _paymentProviderMock = new Mock<IPaymentProvider>();

        var loggerMock = new Mock<ILogger<ProcessWebhookCommandHandler>>();
        _handler = new ProcessWebhookCommandHandler(_context, _paymentProviderMock.Object, loggerMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task ProcessWebhook_FirstTime_ShouldProcessSuccessfully()
    {
        // Arrange
        var eventId = "evt_test_001";
        var payload = "{\"type\": \"payment_intent.succeeded\"}";
        var signature = "t=123,v1=abc";

        var webhookEvent = new PaymentWebhookEvent
        {
            EventId = eventId,
            EventType = "payment_intent.succeeded",
            Provider = "Stripe",
            PaymentIntentId = "pi_test_123",
            Status = "succeeded",
            Amount = 100m,
            Currency = "USD",
            RawPayload = payload
        };

        _paymentProviderMock
            .Setup(p => p.VerifyWebhook(payload, signature))
            .Returns(webhookEvent);

        // Create a payment that the webhook will update
        var order = new Payment.Domain.Entities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m, "USD");
        payment.SetProviderPaymentId("pi_test_123");
        _context.Payments.Add(payment);

        await _context.SaveChangesAsync();

        // Act
        var result = await _handler.Handle(
            new ProcessWebhookCommand(payload, signature), CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);

        var savedEvent = await _context.WebhookEvents
            .FirstOrDefaultAsync(e => e.ProviderEventId == eventId);
        Assert.NotNull(savedEvent);
        Assert.Equal(WebhookEventStatus.Processed, savedEvent.Status);
    }

    [Fact]
    public async Task ProcessWebhook_DuplicateEvent_ShouldReturnSuccessWithoutReprocessing()
    {
        // Arrange
        var eventId = "evt_test_002";
        var payload = "{\"type\": \"payment_intent.succeeded\"}";
        var signature = "t=123,v1=abc";

        // Pre-insert the webhook event (simulating it was already processed)
        var existingEvent = new Payment.Domain.Entities.WebhookEvent("Stripe", eventId, "payment_intent.succeeded", payload);
        existingEvent.MarkProcessed();
        _context.WebhookEvents.Add(existingEvent);
        await _context.SaveChangesAsync();

        var webhookEvent = new PaymentWebhookEvent
        {
            EventId = eventId,
            EventType = "payment_intent.succeeded",
            Provider = "Stripe",
            PaymentIntentId = "pi_test_456",
            Status = "succeeded",
            RawPayload = payload
        };

        _paymentProviderMock
            .Setup(p => p.VerifyWebhook(payload, signature))
            .Returns(webhookEvent);

        // Act
        var result = await _handler.Handle(
            new ProcessWebhookCommand(payload, signature), CancellationToken.None);

        // Assert — duplicate should return success (200)
        Assert.True(result.IsSuccess);

        // Verify only one event exists (no duplicate inserted)
        var eventCount = await _context.WebhookEvents
            .CountAsync(e => e.ProviderEventId == eventId);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public async Task ProcessWebhook_InvalidSignature_ShouldReturnFailure()
    {
        // Arrange
        var payload = "{\"type\": \"payment_intent.succeeded\"}";
        var signature = "invalid_signature";

        _paymentProviderMock
            .Setup(p => p.VerifyWebhook(payload, signature))
            .Throws(new WebhookVerificationException("Invalid signature"));

        // Act
        var result = await _handler.Handle(
            new ProcessWebhookCommand(payload, signature), CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("signature", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessWebhook_UnknownEventType_ShouldRecordButNotFail()
    {
        // Arrange
        var eventId = "evt_test_unknown";
        var payload = "{\"type\": \"some.unknown.event\"}";
        var signature = "t=123,v1=abc";

        var webhookEvent = new PaymentWebhookEvent
        {
            EventId = eventId,
            EventType = "some.unknown.event",
            Provider = "Stripe",
            RawPayload = payload
        };

        _paymentProviderMock
            .Setup(p => p.VerifyWebhook(payload, signature))
            .Returns(webhookEvent);

        // Act
        var result = await _handler.Handle(
            new ProcessWebhookCommand(payload, signature), CancellationToken.None);

        // Assert — unknown events should still return success
        Assert.True(result.IsSuccess);

        var savedEvent = await _context.WebhookEvents
            .FirstOrDefaultAsync(e => e.ProviderEventId == eventId);
        Assert.NotNull(savedEvent);
        Assert.Equal(WebhookEventStatus.Processed, savedEvent.Status);
    }
}
