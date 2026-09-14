using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Payment.Application.Abstractions.Payments;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Domain.Events;
using Payment.Infrastructure.Persistence;
using Payment.Infrastructure.Reconciliation;

namespace Payment.Tests.Infrastructure;

/// <summary>
/// Phase 10 — reconciliation worker tests.
///
/// The worker repairs payments stuck in Pending/Processing by asking the
/// provider for the authoritative state. It must never touch healthy or
/// terminal payments, must respect the stuck threshold, and one bad payment
/// must never stop the batch.
/// </summary>
public class ReconciliationWorkerTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly Mock<IPaymentProvider> _paymentProviderMock = new();
    private readonly string _dbName = Guid.NewGuid().ToString();

    public ReconciliationWorkerTests()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddSingleton(_paymentProviderMock.Object);
        services.AddSingleton(Options.Create(new ReconciliationOptions
        {
            IntervalSeconds = 600,
            StuckThresholdMinutes = 10,
            BatchSize = 20
        }));
        services.AddLogging();
        services.AddSingleton<PaymentReconciliationWorker>();

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

    /// <summary>
    /// Rewinds CreatedAt AND UpdatedAt so the entity passes the stuck-age
    /// threshold. Both must be aged because state transitions (and
    /// SetProviderPaymentId) call Touch(), setting UpdatedAt to now.
    /// Uses reflection because the setters are protected (domain invariant).
    /// </summary>
    private static void AgeEntity(object entity, int minutes)
    {
        var past = DateTime.UtcNow.AddMinutes(-minutes);
        entity.GetType().GetProperty("CreatedAt")!.SetValue(entity, past);
        entity.GetType().GetProperty("UpdatedAt")!.SetValue(entity, past);
    }

    /// <summary>
    /// Seeds a payment stuck in Processing (or Pending) with a paid-eligible
    /// order, aged past the stuck threshold.
    /// </summary>
    private async Task<Payment.Domain.Entities.Payment> SeedStuckPaymentAsync(
        string providerPaymentId,
        bool processing = true,
        bool aged = true,
        bool withProviderId = true)
    {
        using var context = CreateContext();

        var order = new Payment.Domain.Entities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        context.Orders.Add(order);

        var payment = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 100m, "USD");
        if (withProviderId)
            payment.SetProviderPaymentId(providerPaymentId);

        if (processing)
            payment.MarkProcessing();

        context.Payments.Add(payment);
        await context.SaveChangesAsync();

        if (aged)
        {
            AgeEntity(payment, 30);
            await context.SaveChangesAsync();
        }

        return payment;
    }

    [Fact]
    public async Task Reconcile_StuckProcessing_ProviderSaysSucceeded_ShouldRepairPaymentAndOrder()
    {
        var payment = await SeedStuckPaymentAsync("pi_stuck_1");

        _paymentProviderMock
            .Setup(p => p.GetPaymentAsync("pi_stuck_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderPaymentDetails
            {
                ProviderPaymentId = "pi_stuck_1",
                Status = "succeeded",
                Amount = 100m,
                Currency = "USD"
            });

        var worker = _provider.GetRequiredService<PaymentReconciliationWorker>();
        await worker.ReconcileBatchAsync();

        using var verify = CreateContext();
        var repaired = await verify.Payments
            .Include(p => p.Order)
            .SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(PaymentStatus.Succeeded, repaired.Status);
        Assert.Equal(OrderStatus.Paid, repaired.Order.Status);

        // Repair events published via outbox
        Assert.True(await verify.OutboxMessages
            .AnyAsync(m => m.EventType == nameof(PaymentSucceededEvent)));
        Assert.True(await verify.OutboxMessages
            .AnyAsync(m => m.EventType == nameof(OrderPaidEvent)));
    }

    [Fact]
    public async Task Reconcile_StuckPending_ProviderSaysCanceled_ShouldCancelPaymentAndFailOrder()
    {
        var payment = await SeedStuckPaymentAsync("pi_stuck_2", processing: false);

        _paymentProviderMock
            .Setup(p => p.GetPaymentAsync("pi_stuck_2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderPaymentDetails
            {
                ProviderPaymentId = "pi_stuck_2",
                Status = "canceled",
                Amount = 100m,
                Currency = "USD"
            });

        var worker = _provider.GetRequiredService<PaymentReconciliationWorker>();
        await worker.ReconcileBatchAsync();

        using var verify = CreateContext();
        var repaired = await verify.Payments
            .Include(p => p.Order)
            .SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(PaymentStatus.Cancelled, repaired.Status);
        Assert.Equal(OrderStatus.Failed, repaired.Order.Status);

        Assert.True(await verify.OutboxMessages
            .AnyAsync(m => m.EventType == nameof(PaymentFailedEvent)));
    }

    [Fact]
    public async Task Reconcile_ProviderStillProcessing_ShouldNotChangeAnything()
    {
        var payment = await SeedStuckPaymentAsync("pi_stuck_3");

        _paymentProviderMock
            .Setup(p => p.GetPaymentAsync("pi_stuck_3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderPaymentDetails
            {
                ProviderPaymentId = "pi_stuck_3",
                Status = "requires_action",
                Amount = 100m,
                Currency = "USD"
            });

        var worker = _provider.GetRequiredService<PaymentReconciliationWorker>();
        await worker.ReconcileBatchAsync();

        using var verify = CreateContext();
        var unchanged = await verify.Payments.SingleAsync(p => p.Id == payment.Id);

        Assert.Equal(PaymentStatus.Processing, unchanged.Status);
        Assert.Equal(0, await verify.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Reconcile_PaymentWithoutProviderId_ShouldBeSkipped()
    {
        await SeedStuckPaymentAsync("unused", withProviderId: false);

        var worker = _provider.GetRequiredService<PaymentReconciliationWorker>();
        await worker.ReconcileBatchAsync();

        // No provider was consulted at all
        _paymentProviderMock.Verify(
            p => p.GetPaymentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Reconcile_FreshPayment_ShouldBeSkipped()
    {
        await SeedStuckPaymentAsync("pi_fresh", aged: false);

        var worker = _provider.GetRequiredService<PaymentReconciliationWorker>();
        await worker.ReconcileBatchAsync();

        _paymentProviderMock.Verify(
            p => p.GetPaymentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Reconcile_TerminalPayments_ShouldNeverBeSelected()
    {
        using var context = CreateContext();

        var order = new Payment.Domain.Entities.Order(Guid.NewGuid());
        order.MarkPaymentProcessing();
        context.Orders.Add(order);

        var succeeded = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 50m);
        succeeded.SetProviderPaymentId("pi_done");
        succeeded.MarkProcessing();
        succeeded.MarkSucceeded();
        context.Payments.Add(succeeded);

        var failed = new Payment.Domain.Entities.Payment(order.Id, "Stripe", 50m);
        failed.SetProviderPaymentId("pi_failed");
        failed.MarkProcessing();
        failed.MarkFailed();
        context.Payments.Add(failed);

        await context.SaveChangesAsync();
        AgeEntity(succeeded, 30);
        AgeEntity(failed, 30);
        await context.SaveChangesAsync();

        var worker = _provider.GetRequiredService<PaymentReconciliationWorker>();
        await worker.ReconcileBatchAsync();

        _paymentProviderMock.Verify(
            p => p.GetPaymentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Reconcile_OnePaymentFails_ShouldNotStopOtherRepairs()
    {
        var bad = await SeedStuckPaymentAsync("pi_bad");
        var good = await SeedStuckPaymentAsync("pi_good");

        _paymentProviderMock
            .Setup(p => p.GetPaymentAsync("pi_bad", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider down"));

        _paymentProviderMock
            .Setup(p => p.GetPaymentAsync("pi_good", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderPaymentDetails
            {
                ProviderPaymentId = "pi_good",
                Status = "succeeded",
                Amount = 100m,
                Currency = "USD"
            });

        var worker = _provider.GetRequiredService<PaymentReconciliationWorker>();
        await worker.ReconcileBatchAsync();

        using var verify = CreateContext();

        var badPayment = await verify.Payments.SingleAsync(p => p.Id == bad.Id);
        var goodPayment = await verify.Payments.SingleAsync(p => p.Id == good.Id);

        // Bad one untouched (will be retried next cycle); good one repaired
        Assert.Equal(PaymentStatus.Processing, badPayment.Status);
        Assert.Equal(PaymentStatus.Succeeded, goodPayment.Status);
    }
}
