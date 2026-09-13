using Payment.Domain.Common;

namespace Payment.Domain.Entities;

public class WebhookEvent : BaseEntity
{
    public string Provider { get; private set; } = null!;
    public string ProviderEventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public DateTime ReceivedAt { get; private set; }
    public DateTime? ProcessedAt { get; private set; }
    public WebhookEventStatus Status { get; private set; } = WebhookEventStatus.Received;
    public string? Error { get; private set; }
    public string Payload { get; private set; } = null!;

    private WebhookEvent() { } // EF Core constructor

    public WebhookEvent(string provider, string providerEventId, string eventType, string payload)
    {
        Provider = provider;
        ProviderEventId = providerEventId;
        EventType = eventType;
        Payload = payload;
        ReceivedAt = DateTime.UtcNow;
    }

    public void MarkProcessed()
    {
        Status = WebhookEventStatus.Processed;
        ProcessedAt = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = WebhookEventStatus.Failed;
        Error = error;
        ProcessedAt = DateTime.UtcNow;
    }
}

public enum WebhookEventStatus
{
    Received = 0,
    Processed = 1,
    Failed = 2,
    Duplicate = 3
}
