using Payment.Domain.Common;

namespace Payment.Domain.Entities;

/// <summary>
/// Outbox pattern: domain events are persisted in the same database transaction
/// as the state change that produced them. A background worker later publishes
/// them to consumers. This guarantees that a committed state change always has
/// a corresponding event, even if publishing/messaging is temporarily down.
/// </summary>
public class OutboxMessage : BaseEntity
{
    public string EventType { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public OutboxMessageStatus Status { get; private set; } = OutboxMessageStatus.Pending;
    public int RetryCount { get; private set; }
    public string? Error { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    /// <summary>
    /// Messages are not eligible for processing before this time.
    /// Used for exponential backoff between retries.
    /// </summary>
    public DateTime NextAttemptAt { get; private set; } = DateTime.UtcNow;

    private OutboxMessage() { } // EF Core constructor

    public OutboxMessage(string eventType, string payload)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Payload is required.", nameof(payload));

        EventType = eventType;
        Payload = payload;
    }

    public void MarkProcessed()
    {
        Status = OutboxMessageStatus.Processed;
        ProcessedAt = DateTime.UtcNow;
        Error = null;
        Touch();
    }

    /// <summary>
    /// Records a failed publish attempt and schedules the next retry with backoff.
    /// When <paramref name="maxRetries"/> is reached the message is marked permanently Failed.
    /// </summary>
    public void RecordFailure(string error, int maxRetries)
    {
        RetryCount++;
        Error = error;

        if (RetryCount >= maxRetries)
        {
            Status = OutboxMessageStatus.Failed;
            ProcessedAt = DateTime.UtcNow;
        }
        else
        {
            // Exponential backoff: 5s, 25s, 125s... capped at 5 minutes
            var backoffSeconds = Math.Min(Math.Pow(5, RetryCount), 300);
            NextAttemptAt = DateTime.UtcNow.AddSeconds(backoffSeconds);
        }

        Touch();
    }
}

public enum OutboxMessageStatus
{
    Pending = 0,
    Processed = 1,
    Failed = 2
}
