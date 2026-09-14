using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Features.Webhooks.Commands.ProcessWebhook;
using Payment.Domain.Enums;
using Payment.Infrastructure.Caching;
using Payment.Infrastructure.Persistence;
using DomainEntities = Payment.Domain.Entities;

namespace Payment.Tests.Application;

/// <summary>
/// Phase 11 — Redis-backed webhook idempotency fast path.
///
/// After a webhook is fully processed and committed, a marker is written to
/// the cache. Subsequent deliveries of the SAME event are rejected by the
/// cache without any database access. The database idempotency layer (unique
/// constraint on WebhookEvents) remains as the authoritative fallback.
/// </summary>
public class WebhookCacheFastPathTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<IPaymentProvider> _paymentProviderMock;
    private readonly ProcessWebhookCommandHandler _handler;

    public WebhookCacheFastPathTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _paymentProviderMock = new Mock<IPaymentProvider>();

        _handler = new ProcessWebhookCommandHandler(
            _context,
            _paymentProviderMock.Object,
            Mock.Of<ILogger<ProcessWebhookCommandHandler>>(),
            new InMemoryCacheService(),
            new InMemoryDistributedLock());
    }

    public void Dispose() => _context.Dispose();

    private async Task<PaymentWebhookEvent> SeedAndPrepareAsync(string eventId)
    {
        var order = new DomainEntities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        _context.Orders.Add(order);

        var payment = new DomainEntities.Payment(order.Id, "Stripe", 100m, "USD");
        payment.SetProviderPaymentId("pi_fast_path");
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();

        var webhookEvent = new PaymentWebhookEvent
        {
            EventId = eventId,
            EventType = "payment_intent.succeeded",
            Provider = "Stripe",
            PaymentIntentId = "pi_fast_path",
            Status = "succeeded",
            Amount = 100m,
            Currency = "USD",
            RawPayload = "{}"
        };

        _paymentProviderMock
            .Setup(p => p.VerifyWebhook(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(webhookEvent);

        return webhookEvent;
    }

    [Fact]
    public async Task DuplicateWebhook_AfterCommit_IsRejectedByCache()
    {
        await SeedAndPrepareAsync("evt_fast_001");

        var first = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);
        Assert.True(first.IsSuccess);

        var eventsAfterFirst = await _context.WebhookEvents.CountAsync();
        Assert.Equal(1, eventsAfterFirst);

        var paymentAfterFirst = await _context.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Succeeded, paymentAfterFirst.Status);

        // Deliver the SAME event again — must be rejected by the cache fast path
        var second = await _handler.Handle(new ProcessWebhookCommand("{}", "sig"), CancellationToken.None);
        Assert.True(second.IsSuccess);

        // No new database rows: the fast path prevented full reprocessing
        Assert.Equal(1, await _context.WebhookEvents.CountAsync());
        Assert.Equal(1, await _context.OutboxMessages.CountAsync(m => m.EventType == "OrderPaidEvent"));

        // Signature verification still runs (it is cheap and protects the fast path)
        _paymentProviderMock.Verify(p => p.VerifyWebhook(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
    }
}
