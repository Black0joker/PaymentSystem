using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Payment.Application.Abstractions.Outbox;
using Payment.Domain.Entities;
using Payment.Infrastructure.Outbox;
using Payment.Infrastructure.Persistence;

namespace Payment.Tests.Infrastructure;

/// <summary>
/// Fake publisher that captures published events and can be configured to fail.
/// </summary>
internal class FakeOutboxPublisher : IOutboxPublisher
{
    public List<(string EventType, string Payload)> Published { get; } = new();
    public bool ShouldFail { get; set; }
    public string FailureMessage { get; set; } = "broker unavailable";
    public int FailuresBeforeSuccess { get; set; }
    private int _calls;

    public Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default)
    {
        _calls++;
        if (ShouldFail || _calls <= FailuresBeforeSuccess)
            throw new InvalidOperationException(FailureMessage);

        Published.Add((eventType, payload));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Phase 8 — Outbox background worker tests: reliable publishing with retry.
/// </summary>
public class OutboxProcessorTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly FakeOutboxPublisher _publisher = new();
    private readonly string _dbName = Guid.NewGuid().ToString();

    public OutboxProcessorTests()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton<IOutboxPublisher>(_publisher);
        services.AddSingleton(Options.Create(new OutboxOptions
        {
            IntervalSeconds = 1,
            BatchSize = 20,
            MaxRetries = 3
        }));
        services.AddLogging();
        services.AddSingleton<OutboxProcessorWorker>();

        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;
        return new AppDbContext(options);
    }

    private async Task SeedMessageAsync(string eventType = "OrderPaidEvent")
    {
        using var context = CreateContext();
        context.OutboxMessages.Add(new OutboxMessage(eventType, "{\"orderId\":\"x\"}"));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Rewinds NextAttemptAt so the message is due again (backoff window elapsed).
    /// Uses reflection because the property has no public setter (domain invariant).
    /// </summary>
    private async Task RewindNextAttemptAsync()
    {
        using var context = CreateContext();
        var message = await context.OutboxMessages.SingleAsync();
        typeof(OutboxMessage)
            .GetProperty(nameof(OutboxMessage.NextAttemptAt))!
            .SetValue(message, DateTime.UtcNow.AddSeconds(-1));
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task PendingMessage_IsPublishedAndMarkedProcessed()
    {
        await SeedMessageAsync();
        var worker = _provider.GetRequiredService<OutboxProcessorWorker>();

        await worker.ProcessBatchAsync();

        Assert.Single(_publisher.Published);
        Assert.Equal("OrderPaidEvent", _publisher.Published[0].EventType);

        using var context = CreateContext();
        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.Processed, message.Status);
        Assert.NotNull(message.ProcessedAt);
        Assert.Null(message.Error);
    }

    [Fact]
    public async Task PublishFailure_IncrementsRetryAndSchedulesBackoff()
    {
        await SeedMessageAsync();
        _publisher.ShouldFail = true;

        var worker = _provider.GetRequiredService<OutboxProcessorWorker>();
        await worker.ProcessBatchAsync();

        Assert.Empty(_publisher.Published);

        using var context = CreateContext();
        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.Pending, message.Status);
        Assert.Equal(1, message.RetryCount);
        Assert.Equal("broker unavailable", message.Error);
        Assert.True(message.NextAttemptAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task RetryThenSuccess_EventPublishedOnce()
    {
        await SeedMessageAsync();
        _publisher.FailuresBeforeSuccess = 1; // first attempt fails, second succeeds

        var worker = _provider.GetRequiredService<OutboxProcessorWorker>();
        await worker.ProcessBatchAsync(); // attempt 1 -> fail, backoff scheduled

        using (var context = CreateContext())
        {
            var message = await context.OutboxMessages.SingleAsync();
            Assert.Equal(OutboxMessageStatus.Pending, message.Status);
            Assert.Equal(1, message.RetryCount);
        }

        await RewindNextAttemptAsync(); // simulate backoff window elapsing
        await worker.ProcessBatchAsync(); // attempt 2 -> success

        Assert.Single(_publisher.Published);

        using (var context = CreateContext())
        {
            var message = await context.OutboxMessages.SingleAsync();
            Assert.Equal(OutboxMessageStatus.Processed, message.Status);
        }
    }

    [Fact]
    public async Task ExhaustedRetries_MarkedFailed_NoSilentLoss()
    {
        await SeedMessageAsync();
        _publisher.ShouldFail = true;

        var worker = _provider.GetRequiredService<OutboxProcessorWorker>();

        // MaxRetries = 3: fail, rewind, fail, rewind, fail -> Failed
        for (var i = 0; i < 3; i++)
        {
            await worker.ProcessBatchAsync();

            using var context = CreateContext();
            var message = await context.OutboxMessages.SingleAsync();
            if (message.Status == OutboxMessageStatus.Failed)
                break;

            await RewindNextAttemptAsync();
        }

        using var finalContext = CreateContext();
        var finalMessage = await finalContext.OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.Failed, finalMessage.Status);
        Assert.Equal(3, finalMessage.RetryCount);
        Assert.Empty(_publisher.Published);
    }

    [Fact]
    public async Task FutureScheduledMessage_IsSkipped()
    {
        await SeedMessageAsync();
        _publisher.ShouldFail = true;

        var worker = _provider.GetRequiredService<OutboxProcessorWorker>();
        await worker.ProcessBatchAsync(); // fails -> NextAttemptAt scheduled in future

        // Second cycle: message is not due yet -> skipped, retry count unchanged
        await worker.ProcessBatchAsync();

        using var context = CreateContext();
        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(1, message.RetryCount);
        Assert.Empty(_publisher.Published);
    }
}
