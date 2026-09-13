using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Features.Payments.Commands.CreateCheckout;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Infrastructure.Persistence;

namespace Payment.Tests.Application;

/// <summary>
/// Phase 7 — Failure handling for checkout.
/// When the provider call fails AFTER our DB commit, the system must compensate:
/// attempt -> Failed, payment -> Cancelled, order -> back to Pending (retryable).
/// </summary>
public class CheckoutFailureHandlingTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly Mock<IPaymentProvider> _providerMock;
    private readonly CreateCheckoutCommandHandler _handler;
    private readonly Order _order;

    public CheckoutFailureHandlingTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _providerMock = new Mock<IPaymentProvider>();
        _handler = new CreateCheckoutCommandHandler(
            _context, _providerMock.Object, Mock.Of<ILogger<CreateCheckoutCommandHandler>>());

        // Seed: user + course + pending order with one item
        var user = new User("student@test.com", "Test", "Student");
        _context.Users.Add(user);

        var course = new Course("C# Basics", "Learn C#", 100m);
        _context.Courses.Add(course);

        _order = new Order(user.Id);
        _order.AddItem(course, quantity: 1);
        _context.Orders.Add(_order);

        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private CreateCheckoutCommand Command() =>
        new(_order.Id, "https://app/success", "https://app/cancel");

    [Fact]
    public async Task ProviderTimeout_Compensates_OrderReturnsToPending()
    {
        _providerMock
            .Setup(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Provider timed out"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        // Checkout fails
        Assert.False(result.IsSuccess);

        // Compensated state: everything retryable
        Assert.Equal(OrderStatus.Pending, _order.Status);

        var payment = await _context.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Null(payment.ProviderPaymentId); // never reached the provider

        var attempt = await _context.PaymentAttempts.SingleAsync();
        Assert.Equal(PaymentStatus.Failed, attempt.Status);
        Assert.NotNull(attempt.FailureReason);
    }

    [Fact]
    public async Task ProviderFailure_ThenRecovery_RetrySucceeds()
    {
        // First attempt: provider unavailable
        _providerMock
            .Setup(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("503 Service Unavailable"));

        var first = await _handler.Handle(Command(), CancellationToken.None);
        Assert.False(first.IsSuccess);
        Assert.Equal(OrderStatus.Pending, _order.Status);

        // Provider recovers
        _providerMock
            .Setup(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutSessionResult
            {
                SessionId = "cs_test_retry",
                CheckoutUrl = "https://checkout/test",
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            });

        var second = await _handler.Handle(Command(), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(OrderStatus.PaymentProcessing, _order.Status);

        // The retry created a NEW payment (the cancelled one is history)
        var payments = await _context.Payments.CountAsync();
        Assert.Equal(2, payments);

        var active = await _context.Payments
            .FirstAsync(p => p.Status == PaymentStatus.Pending);
        Assert.Equal("cs_test_retry", active.ProviderPaymentId);
    }

    [Fact]
    public async Task HappyPath_CreatesPaymentAttemptAndSession()
    {
        _providerMock
            .Setup(p => p.CreateCheckoutSessionAsync(It.IsAny<CheckoutRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutSessionResult
            {
                SessionId = "cs_test_123",
                CheckoutUrl = "https://checkout/test",
                ExpiresAt = DateTime.UtcNow.AddMinutes(30)
            });

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(_order.Id, result.Value!.OrderId);
        Assert.Equal("https://checkout/test", result.Value.CheckoutUrl);
        Assert.Equal(OrderStatus.PaymentProcessing, _order.Status);

        var payment = await _context.Payments.SingleAsync();
        Assert.Equal("cs_test_123", payment.ProviderPaymentId);
        Assert.Equal(100m, payment.Amount);
    }
}
