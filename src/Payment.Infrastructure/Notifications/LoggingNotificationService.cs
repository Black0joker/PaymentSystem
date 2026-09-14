using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Notifications;
using Payment.Domain.Events;

namespace Payment.Infrastructure.Notifications;

/// <summary>
/// Phase 12 — default notification channel. In production this would send
/// email/push via an external provider; here it logs so the pipeline is
/// fully testable without third-party accounts.
/// </summary>
public class LoggingNotificationService : INotificationService
{
    private readonly ILogger<LoggingNotificationService> _logger;

    public LoggingNotificationService(ILogger<LoggingNotificationService> logger)
    {
        _logger = logger;
    }

    public Task NotifyOrderPaidAsync(OrderPaidEvent evt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Notification] Order {OrderId} paid \u2014 sending confirmation to user {UserId}. Amount={Total} {Currency}",
            evt.OrderId, evt.UserId, evt.Total, evt.Currency);
        return Task.CompletedTask;
    }

    public Task NotifyOrderRefundedAsync(OrderRefundedEvent evt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Notification] Order {OrderId} refunded \u2014 notifying customer. Amount={Amount} {Currency}",
            evt.OrderId, evt.Amount, evt.Currency);
        return Task.CompletedTask;
    }

    public Task NotifyPaymentFailedAsync(PaymentFailedEvent evt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Notification] Payment {PaymentId} failed \u2014 notifying customer. Reason={Reason}",
            evt.PaymentId, evt.Reason);
        return Task.CompletedTask;
    }
}
