using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.Application.Abstractions.Outbox;
using Payment.Domain.Entities;
using Payment.Infrastructure.Persistence;

namespace Payment.Infrastructure.Outbox;

/// <summary>
/// Phase 8 — Outbox pattern background worker.
///
/// Reliably publishes domain events that were written to the OutboxMessages table
/// inside the same database transaction as the state change that produced them.
///
/// Guarantees:
///  - State change committed  => message exists in the outbox  (same transaction)
///  - Message exists          => will eventually be published  (this worker + retry)
///  - Publisher down          => messages stay Pending and are retried with backoff
///
/// A scoped DbContext is created per cycle because the worker is a singleton
/// while DbContext is scoped.
/// </summary>
public class OutboxProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxPublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessorWorker> _logger;

    public OutboxProcessorWorker(
        IServiceScopeFactory scopeFactory,
        IOutboxPublisher publisher,
        IOptions<OutboxOptions> options,
        ILogger<OutboxProcessorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox worker started. Interval={Interval}s, BatchSize={Batch}, MaxRetries={MaxRetries}",
            _options.IntervalSeconds, _options.BatchSize, _options.MaxRetries);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The worker must never crash the host — log and continue polling.
                _logger.LogError(ex, "Outbox processing cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Processes one batch of due outbox messages. Public so it can be invoked
    /// directly in tests without running the full background loop.
    /// </summary>
    public async Task ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;

        var messages = await context.OutboxMessages
            .Where(m => m.Status == OutboxMessageStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .ThenBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return;

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await _publisher.PublishAsync(message.EventType, message.Payload, cancellationToken);
                message.MarkProcessed();

                _logger.LogInformation(
                    "Outbox message published. Id={Id}, EventType={EventType}",
                    message.Id, message.EventType);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Retry policy: record failure, schedule next attempt with backoff.
                // After MaxRetries the message is marked permanently Failed for
                // manual inspection — it is never silently lost.
                message.RecordFailure(ex.Message, _options.MaxRetries);

                _logger.LogError(ex,
                    "Failed to publish outbox message. Id={Id}, EventType={EventType}, RetryCount={RetryCount}, Status={Status}",
                    message.Id, message.EventType, message.RetryCount, message.Status);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
