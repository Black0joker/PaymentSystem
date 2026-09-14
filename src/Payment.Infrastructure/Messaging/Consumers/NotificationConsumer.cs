using MassTransit;
using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Notifications;
using Payment.Domain.Events;

namespace Payment.Infrastructure.Messaging.Consumers;

/// <summary>
/// Phase 12 — customer-facing side effects of payment events.
/// Decoupled from the payment flow: the payment completes even if this
/// consumer is slow or temporarily unavailable (MassTransit retries).
/// </summary>
public class NotificationConsumer :
    IConsumer<OrderPaidEvent>,
    IConsumer<OrderRefundedEvent>,
    IConsumer<PaymentFailedEvent>
{
    private readonly INotificationService _notifications;
    private readonly ILogger<NotificationConsumer> _logger;

    public NotificationConsumer(INotificationService notifications, ILogger<NotificationConsumer> logger)
    {
        _notifications = notifications;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderPaidEvent> context)
    {
        await _notifications.NotifyOrderPaidAsync(context.Message, context.CancellationToken);
        _logger.LogDebug("NotificationConsumer: order paid notification sent for {OrderId}.", context.Message.OrderId);
    }

    public async Task Consume(ConsumeContext<OrderRefundedEvent> context)
    {
        await _notifications.NotifyOrderRefundedAsync(context.Message, context.CancellationToken);
        _logger.LogDebug("NotificationConsumer: refund notification sent for {OrderId}.", context.Message.OrderId);
    }

    public async Task Consume(ConsumeContext<PaymentFailedEvent> context)
    {
        await _notifications.NotifyPaymentFailedAsync(context.Message, context.CancellationToken);
        _logger.LogDebug("NotificationConsumer: failure notification sent for {PaymentId}.", context.Message.PaymentId);
    }
}
