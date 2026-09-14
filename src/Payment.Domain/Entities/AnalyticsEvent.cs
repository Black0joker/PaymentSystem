using Payment.Domain.Common;

namespace Payment.Domain.Entities;

/// <summary>
/// Phase 12 — append-only record written by the analytics consumer for
/// every published payment domain event. Demonstrates event-driven side
/// effects: analytics is decoupled from the payment flow and survives
/// independently of it.
/// </summary>
public class AnalyticsEvent : BaseEntity
{
    public string EventType { get; private set; } = null!;
    public Guid EntityId { get; private set; }
    public string Payload { get; private set; } = null!;
    public DateTime OccurredAt { get; private set; }

    private AnalyticsEvent() { } // EF Core constructor

    public AnalyticsEvent(string eventType, Guid entityId, string payload, DateTime occurredAt)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType is required.", nameof(eventType));

        EventType = eventType;
        EntityId = entityId;
        Payload = payload ?? "{}";
        OccurredAt = occurredAt;
    }
}
