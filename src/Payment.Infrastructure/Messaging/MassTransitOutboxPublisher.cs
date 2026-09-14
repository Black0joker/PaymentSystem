using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Outbox;
using Payment.Domain.Events;

namespace Payment.Infrastructure.Messaging;

/// <summary>
/// Phase 12 — outbox publisher backed by MassTransit.
///
/// This is the bridge between the transactional outbox (SQL Server, the
/// reliability boundary) and the message broker (RabbitMQ/in-memory, the
/// distribution boundary):
///
///   state change + OutboxMessage  ->  same SQL transaction
///   this publisher                ->  deserializes the stored event and
///                                     publishes it to the bus
///   consumers                     ->  enrollment, notification, analytics
///
/// If the broker is down, PublishAsync throws, the outbox worker applies its
/// retry/backoff policy, and nothing is lost — the SQL commit already
/// happened. This is exactly the guarantee the outbox pattern exists for.
/// </summary>
public class MassTransitOutboxPublisher : IOutboxPublisher
{
    private static readonly Dictionary<string, Type> EventTypes = new()
    {
        [nameof(OrderPaidEvent)] = typeof(OrderPaidEvent),
        [nameof(PaymentSucceededEvent)] = typeof(PaymentSucceededEvent),
        [nameof(PaymentFailedEvent)] = typeof(PaymentFailedEvent),
        [nameof(OrderRefundedEvent)] = typeof(OrderRefundedEvent)
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MassTransitOutboxPublisher> _logger;

    public MassTransitOutboxPublisher(
        IServiceScopeFactory scopeFactory,
        ILogger<MassTransitOutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default)
    {
        if (!EventTypes.TryGetValue(eventType, out var messageType))
        {
            // Unknown events are a data bug — throw so the outbox worker
            // marks the message Failed after retries instead of dropping it.
            throw new InvalidOperationException($"Unknown outbox event type: {eventType}");
        }

        var message = JsonSerializer.Deserialize(payload, messageType)
            ?? throw new InvalidOperationException($"Failed to deserialize {eventType} payload.");

        // IPublishEndpoint is scoped; the outbox worker may call us from any
        // context, so publish inside a dedicated scope.
        using var scope = _scopeFactory.CreateScope();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        await publishEndpoint.Publish(message, messageType, cancellationToken);

        _logger.LogDebug("Published {EventType} to message bus.", eventType);
    }
}
