namespace Payment.Infrastructure.Reconciliation;

/// <summary>
/// Configuration for the payment reconciliation background worker.
/// Bound from the "Reconciliation" configuration section.
/// </summary>
public class ReconciliationOptions
{
    public const string SectionName = "Reconciliation";

    /// <summary>Delay between reconciliation cycles.</summary>
    public int IntervalSeconds { get; set; } = 600;

    /// <summary>
    /// A payment must sit in Pending/Processing for at least this many minutes
    /// before reconciliation considers it stuck.
    /// </summary>
    public int StuckThresholdMinutes { get; set; } = 10;

    /// <summary>Maximum payments reconciled per cycle.</summary>
    public int BatchSize { get; set; } = 20;
}
