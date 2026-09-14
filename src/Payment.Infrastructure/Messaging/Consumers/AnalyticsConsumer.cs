using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using Payment.Domain.Entities;
using Payment.Domain.Events;
using Payment.Infrastructure.Persistence;

namespace Payment.Infrastructure.Messaging.Consumers;

/// <summary>
/// Phase 12 — records every payment domain event into an append-only
/// analytics table. Demonstrates a fully decoupled event-driven consumer:
/// the payment flow never knows analytics exists.
/// </summary>
public class AnalyticsConsumer :
    IConsumer<PaymentSucceededEvent>,
    IConsumer<PaymentFailedEvent>,
    IConsumer<OrderPaidEvent>,
    IConsumer<OrderRefundedEvent>
{
    private readonly AppDbContext _context;
    private readonly ILogger<AnalyticsConsumer> _logger;

    public AnalyticsConsumer(AppDbContext context, ILogger<AnalyticsConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<PaymentSucceededEvent> context) =>
        RecordAsync(nameof(PaymentSucceededEvent), context.Message.PaymentId, context.Message, context.Message.OccurredAt, context.CancellationToken);

    public Task Consume(ConsumeContext<PaymentFailedEvent> context) =>
        RecordAsync(nameof(PaymentFailedEvent), context.Message.PaymentId, context.Message, context.Message.OccurredAt, context.CancellationToken);

    public Task Consume(ConsumeContext<OrderPaidEvent> context) =>
        RecordAsync(nameof(OrderPaidEvent), context.Message.OrderId, context.Message, context.Message.OccurredAt, context.CancellationToken);

    public Task Consume(ConsumeContext<OrderRefundedEvent> context) =>
        RecordAsync(nameof(OrderRefundedEvent), context.Message.OrderId, context.Message, context.Message.OccurredAt, context.CancellationToken);

    private async Task RecordAsync<TMessage>(
        string eventType, Guid entityId, TMessage message, DateTime occurredAt, CancellationToken cancellationToken)
    {
        _context.AnalyticsEvents.Add(new AnalyticsEvent(
            eventType,
            entityId,
            JsonSerializer.Serialize(message),
            occurredAt));

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("AnalyticsConsumer: recorded {EventType} for {EntityId}.", eventType, entityId);
    }
}
