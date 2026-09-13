using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Outbox;

namespace Payment.Infrastructure.Outbox;

/// <summary>
/// Default publisher: logs the event. In later phases this is replaced by a
/// RabbitMQ/MassTransit publisher — the worker and handler code never change.
/// </summary>
public class LoggingOutboxPublisher : IOutboxPublisher
{
    private readonly ILogger<LoggingOutboxPublisher> _logger;

    public LoggingOutboxPublisher(ILogger<LoggingOutboxPublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Outbox event published. EventType={EventType}, Payload={Payload}",
            eventType, payload);

        return Task.CompletedTask;
    }
}
