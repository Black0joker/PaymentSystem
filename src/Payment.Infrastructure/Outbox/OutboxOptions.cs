namespace Payment.Infrastructure.Outbox;

/// <summary>
/// Configuration for the outbox background worker.
/// Bound from the "Outbox" configuration section.
/// </summary>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Delay between polling cycles.</summary>
    public int IntervalSeconds { get; set; } = 5;

    /// <summary>Maximum messages processed per cycle.</summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>After this many failed attempts a message is marked permanently Failed.</summary>
    public int MaxRetries { get; set; } = 5;
}
