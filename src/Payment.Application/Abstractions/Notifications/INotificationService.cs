using Payment.Domain.Events;

namespace Payment.Application.Abstractions.Notifications;

/// <summary>
/// Phase 12 — side-effect channel for customer-facing notifications
/// (email/push). Consumed by the notification consumer that reacts to
/// published domain events. Kept in the Application layer so consumers and
/// tests depend only on the abstraction.
/// </summary>
public interface INotificationService
{
    Task NotifyOrderPaidAsync(OrderPaidEvent evt, CancellationToken cancellationToken = default);
    Task NotifyOrderRefundedAsync(OrderRefundedEvent evt, CancellationToken cancellationToken = default);
    Task NotifyPaymentFailedAsync(PaymentFailedEvent evt, CancellationToken cancellationToken = default);
}
