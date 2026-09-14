using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Payment.Application.Abstractions.Outbox;
using Payment.Domain.Entities;
using Payment.Domain.Events;
using Payment.Infrastructure.Messaging;
using Payment.Infrastructure.Outbox;
using Payment.Infrastructure.Persistence;

namespace Payment.Tests.Infrastructure;

/// <summary>
/// Phase 12 — outbox-to-bus bridge.
///
///  1. Unit level: the publisher deserializes stored payloads and hands them
///     to IPublishEndpoint with the correct CLR type.
///  2. Integration level: a real in-memory MassTransit bus delivers the
///     published event to a consumer, driven end-to-end by the outbox worker
///     (outbox row -> publisher -> bus -> consumer).
/// </summary>
public class MassTransitOutboxPublisherTests
{
    private static string OrderPaidPayload(Guid orderId) => JsonSerializer.Serialize(new OrderPaidEvent(
        orderId, Guid.NewGuid(), Guid.NewGuid(), 100m, "USD", DateTime.UtcNow));

    [Fact]
    public async Task PublishAsync_KnownEventType_PublishesDeserializedMessage()
    {
        var orderId = Guid.NewGuid();

        var publishEndpoint = new Mock<IPublishEndpoint>();
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(sp => sp.GetService(typeof(IPublishEndpoint)))
            .Returns(publishEndpoint.Object);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(serviceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var publisher = new MassTransitOutboxPublisher(
            scopeFactory.Object, Mock.Of<ILogger<MassTransitOutboxPublisher>>());

        await publisher.PublishAsync("OrderPaidEvent", OrderPaidPayload(orderId));

        publishEndpoint.Verify(p => p.Publish(
                It.IsAny<object>(),
                typeof(OrderPaidEvent),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Capture the actual message to assert deserialization correctness
        object? publishedMessage = null;
        publishEndpoint
            .Setup(p => p.Publish(It.IsAny<object>(), typeof(OrderPaidEvent), It.IsAny<CancellationToken>()))
            .Callback<object, Type, CancellationToken>((m, t, ct) => publishedMessage = m)
            .Returns(Task.CompletedTask);

        await publisher.PublishAsync("OrderPaidEvent", OrderPaidPayload(orderId));

        var evt = Assert.IsType<OrderPaidEvent>(publishedMessage);
        Assert.Equal(orderId, evt.OrderId);
    }

    [Fact]
    public async Task PublishAsync_UnknownEventType_Throws()
    {
        var scopeFactory = new Mock<IServiceScopeFactory>();
        var publisher = new MassTransitOutboxPublisher(
            scopeFactory.Object, Mock.Of<ILogger<MassTransitOutboxPublisher>>());

        // Unknown events must fail loudly so the outbox marks them Failed
        // after retries — never silently dropped.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync("SomethingElseEvent", "{}"));
    }

    /// <summary>Records the first OrderPaidEvent delivered over the bus.</summary>
    public class RecordingConsumer : IConsumer<OrderPaidEvent>
    {
        public static readonly TaskCompletionSource<OrderPaidEvent> Received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Consume(ConsumeContext<OrderPaidEvent> context)
        {
            Received.TrySetResult(context.Message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task OutboxWorker_WithMassTransitPublisher_DeliversEventToConsumer()
    {
        var orderId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddMassTransit(x =>
        {
            x.AddConsumer<RecordingConsumer>();
            x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
        });

        await using var provider = services.BuildServiceProvider();

        // Start the in-memory bus
        var busControl = provider.GetRequiredService<IBusControl>();
        await busControl.StartAsync();

        try
        {
            // Seed a committed outbox row (as the webhook handler would)
            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                context.OutboxMessages.Add(new OutboxMessage("OrderPaidEvent", OrderPaidPayload(orderId)));
                await context.SaveChangesAsync();
            }

            var publisher = new MassTransitOutboxPublisher(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<ILogger<MassTransitOutboxPublisher>>());

            var worker = new OutboxProcessorWorker(
                provider.GetRequiredService<IServiceScopeFactory>(),
                publisher,
                Options.Create(new OutboxOptions()),
                Mock.Of<ILogger<OutboxProcessorWorker>>());

            // Outbox cycle: row -> MassTransit publisher -> bus -> consumer
            await worker.ProcessBatchAsync();

            var received = await RecordingConsumer.Received.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(orderId, received.OrderId);

            // The outbox row is marked Processed only after successful publish
            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var message = await context.OutboxMessages.SingleAsync();
                Assert.Equal(OutboxMessageStatus.Processed, message.Status);
            }
        }
        finally
        {
            await busControl.StopAsync();
        }
    }
}
