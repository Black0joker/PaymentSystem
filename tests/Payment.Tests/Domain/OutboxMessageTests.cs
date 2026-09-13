using Payment.Domain.Entities;

namespace Payment.Tests.Domain;

public class OutboxMessageTests
{
    [Fact]
    public void NewMessage_IsPending_WithDueNextAttemptAt()
    {
        var message = new OutboxMessage("OrderPaidEvent", "{}");

        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal(0, message.RetryCount);
        Assert.True(message.NextAttemptAt <= DateTime.UtcNow);
        Assert.Null(message.ProcessedAt);
    }

    [Fact]
    public void Constructor_RejectsEmptyEventType()
    {
        Assert.Throws<ArgumentException>(() => new OutboxMessage("", "{}"));
    }

    [Fact]
    public void Constructor_RejectsEmptyPayload()
    {
        Assert.Throws<ArgumentException>(() => new OutboxMessage("OrderPaidEvent", ""));
    }

    [Fact]
    public void MarkProcessed_SetsStatusAndTimestamp()
    {
        var message = new OutboxMessage("OrderPaidEvent", "{}");

        message.MarkProcessed();

        Assert.Equal(OutboxMessageStatus.Processed, message.Status);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public void RecordFailure_BelowMaxRetries_StaysPendingWithBackoff()
    {
        var message = new OutboxMessage("OrderPaidEvent", "{}");
        var before = DateTime.UtcNow;

        message.RecordFailure("broker down", maxRetries: 5);

        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal(1, message.RetryCount);
        Assert.Equal("broker down", message.Error);
        Assert.True(message.NextAttemptAt > before); // backoff scheduled in the future
    }

    [Fact]
    public void RecordFailure_ReachingMaxRetries_MarksFailed()
    {
        var message = new OutboxMessage("OrderPaidEvent", "{}");

        for (var i = 0; i < 4; i++)
            message.RecordFailure($"attempt {i}", maxRetries: 5);

        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal(4, message.RetryCount);

        message.RecordFailure("final attempt", maxRetries: 5);

        Assert.Equal(OutboxMessageStatus.Failed, message.Status);
        Assert.Equal(5, message.RetryCount);
        Assert.NotNull(message.ProcessedAt);
    }
}
