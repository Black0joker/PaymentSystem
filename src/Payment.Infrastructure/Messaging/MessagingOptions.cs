namespace Payment.Infrastructure.Messaging;

/// <summary>
/// Configuration for the message broker layer. Bound from the "Messaging"
/// configuration section.
///
/// Provider values:
///  - "InMemory": MassTransit in-memory transport (local development, tests).
///  - "RabbitMQ": MassTransit over RabbitMQ (production).
///  - "None":    no bus is started; the outbox publisher only logs
///                (the Phase 8 behavior).
///
/// The outbox remains the reliability boundary: whatever transport is
/// chosen, messages are first committed to SQL and only then published.
/// </summary>
public class MessagingOptions
{
    public const string SectionName = "Messaging";

    public const string ProviderInMemory = "InMemory";
    public const string ProviderRabbitMq = "RabbitMQ";
    public const string ProviderNone = "None";

    public string Provider { get; set; } = ProviderInMemory;

    public RabbitMqOptions RabbitMq { get; set; } = new();

    public class RabbitMqOptions
    {
        public string Host { get; set; } = "localhost";
        public string VirtualHost { get; set; } = "/";
        public string Username { get; set; } = "guest";
        public string Password { get; set; } = "guest";
    }
}
