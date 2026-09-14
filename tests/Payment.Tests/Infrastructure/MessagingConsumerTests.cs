using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Application.Abstractions.Notifications;
using Payment.Domain.Entities;
using Payment.Domain.Events;
using Payment.Infrastructure.Messaging.Consumers;
using Payment.Infrastructure.Persistence;
using DomainEntities = Payment.Domain.Entities;

namespace Payment.Tests.Infrastructure;

/// <summary>
/// Phase 12 — consumer behavior: enrollment idempotency, notification
/// fan-out, analytics recording. Consumers are exercised directly with a
/// mocked ConsumeContext — no broker needed at this layer.
/// </summary>
public class MessagingConsumerTests : IDisposable
{
    private readonly AppDbContext _context;

    public MessagingConsumerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    private static ConsumeContext<T> MockConsumeContext<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private async Task<(DomainEntities.Order order, Guid userId, Guid courseId)> SeedPaidOrderAsync()
    {
        var userId = Guid.NewGuid();
        var course = new Course("Test Course", "Description", 100m);
        _context.Courses.Add(course);

        var order = new DomainEntities.Order(userId);
        order.AddItem(course);
        order.MarkPaymentProcessing();
        order.MarkPaid();
        _context.Orders.Add(order);

        await _context.SaveChangesAsync();
        return (order, userId, course.Id);
    }

    [Fact]
    public async Task EnrollmentConsumer_OrderPaid_CreatesEnrollment()
    {
        var (order, userId, courseId) = await SeedPaidOrderAsync();

        var consumer = new EnrollmentConsumer(
            _context, Mock.Of<ILogger<EnrollmentConsumer>>());

        await consumer.Consume(MockConsumeContext(new OrderPaidEvent(
            order.Id, userId, Guid.NewGuid(), order.Total, order.Currency, DateTime.UtcNow)));

        var enrollments = await _context.Enrollments.ToListAsync();
        var enrollment = Assert.Single(enrollments);
        Assert.Equal(userId, enrollment.UserId);
        Assert.Equal(courseId, enrollment.CourseId);
        Assert.Equal(order.Id, enrollment.OrderId);
    }

    [Fact]
    public async Task EnrollmentConsumer_DuplicateDelivery_DoesNotDuplicateEnrollment()
    {
        var (order, userId, _) = await SeedPaidOrderAsync();

        var consumer = new EnrollmentConsumer(
            _context, Mock.Of<ILogger<EnrollmentConsumer>>());

        var evt = new OrderPaidEvent(
            order.Id, userId, Guid.NewGuid(), order.Total, order.Currency, DateTime.UtcNow);

        await consumer.Consume(MockConsumeContext(evt));
        await consumer.Consume(MockConsumeContext(evt)); // provider redelivery

        Assert.Equal(1, await _context.Enrollments.CountAsync());
    }

    [Fact]
    public async Task AnalyticsConsumer_RecordsEveryEventType()
    {
        var consumer = new AnalyticsConsumer(
            _context, Mock.Of<ILogger<AnalyticsConsumer>>());

        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await consumer.Consume(MockConsumeContext(new PaymentSucceededEvent(paymentId, orderId, "pi_1", 100m, "USD", DateTime.UtcNow)));
        await consumer.Consume(MockConsumeContext(new PaymentFailedEvent(paymentId, orderId, "card_declined", DateTime.UtcNow)));
        await consumer.Consume(MockConsumeContext(new OrderPaidEvent(orderId, Guid.NewGuid(), paymentId, 100m, "USD", DateTime.UtcNow)));
        await consumer.Consume(MockConsumeContext(new OrderRefundedEvent(orderId, paymentId, 100m, "USD", DateTime.UtcNow)));

        var events = await _context.AnalyticsEvents.ToListAsync();
        Assert.Equal(4, events.Count);
        Assert.Contains(events, e => e.EventType == "PaymentSucceededEvent");
        Assert.Contains(events, e => e.EventType == "PaymentFailedEvent");
        Assert.Contains(events, e => e.EventType == "OrderPaidEvent");
        Assert.Contains(events, e => e.EventType == "OrderRefundedEvent");
    }

    [Fact]
    public async Task NotificationConsumer_DelegatesToNotificationService()
    {
        var notifications = new Mock<INotificationService>();
        var consumer = new NotificationConsumer(
            notifications.Object, Mock.Of<ILogger<NotificationConsumer>>());

        var paidEvt = new OrderPaidEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m, "USD", DateTime.UtcNow);
        var refundedEvt = new OrderRefundedEvent(paidEvt.OrderId, paidEvt.PaymentId, 100m, "USD", DateTime.UtcNow);
        var failedEvt = new PaymentFailedEvent(paidEvt.PaymentId, paidEvt.OrderId, "card_declined", DateTime.UtcNow);

        await consumer.Consume(MockConsumeContext(paidEvt));
        await consumer.Consume(MockConsumeContext(refundedEvt));
        await consumer.Consume(MockConsumeContext(failedEvt));

        notifications.Verify(n => n.NotifyOrderPaidAsync(paidEvt, It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.NotifyOrderRefundedAsync(refundedEvt, It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(n => n.NotifyPaymentFailedAsync(failedEvt, It.IsAny<CancellationToken>()), Times.Once);
    }
}
