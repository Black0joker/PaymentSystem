namespace Payment.Application.Abstractions.Outbox;

/// <summary>
/// Abstraction over the transport used to publish outbox messages to consumers
/// (message broker, in-process event bus, HTTP, etc.).
/// The Application layer never knows which transport is used.
/// </summary>
public interface IOutboxPublisher
{
    /// <summary>
    /// Publishes a single outbox message. Throw on failure so the worker can
    /// record the error and retry according to its retry policy.
    /// </summary>
    Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default);
}
