namespace Payment.Domain.Events;

/// <summary>
/// Domain events raised by state transitions.
/// They are serialized into the outbox within the same transaction
/// as the state change, then published by the outbox worker.
/// </summary>
public record OrderPaidEvent(
    Guid OrderId,
    Guid UserId,
    Guid PaymentId,
    decimal Total,
    string Currency,
    DateTime OccurredAt);

public record PaymentSucceededEvent(
    Guid PaymentId,
    Guid OrderId,
    string ProviderPaymentId,
    decimal Amount,
    string Currency,
    DateTime OccurredAt);

public record PaymentFailedEvent(
    Guid PaymentId,
    Guid OrderId,
    string? Reason,
    DateTime OccurredAt);

public record OrderRefundedEvent(
    Guid OrderId,
    Guid PaymentId,
    decimal Amount,
    string Currency,
    DateTime OccurredAt);
