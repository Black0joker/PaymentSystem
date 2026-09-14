using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Payment.Application.Abstractions.Caching;
using Payment.Application.Common;
using Payment.Application.Features.Payments.Queries.GetPayment;
using Payment.Domain.Enums;
using Payment.Infrastructure.Caching;
using DomainEntities = Payment.Domain.Entities;
using Payment.Infrastructure.Persistence;

namespace Payment.Tests.Application;

/// <summary>
/// Phase 11 — cache-aside behavior of the payment status read path.
///
/// Invariants under test:
///  - the database is always the fallback (misses hit SQL)
///  - a hot entry is served from the cache even if SQL changes underneath
///  - invalidation (RemoveAsync) forces the next read back to SQL
/// </summary>
public class PaymentStatusCacheTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly InMemoryCacheService _cache;
    private readonly GetPaymentQueryHandler _handler;

    public PaymentStatusCacheTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        _cache = new InMemoryCacheService();
        _handler = new GetPaymentQueryHandler(_context, _cache);
    }

    public void Dispose() => _context.Dispose();

    private async Task<DomainEntities.Payment> SeedPaymentAsync()
    {
        var order = new DomainEntities.Order(Guid.NewGuid());
        _context.Orders.Add(order);

        var payment = new DomainEntities.Payment(order.Id, "Stripe", 100m, "USD");
        _context.Payments.Add(payment);

        await _context.SaveChangesAsync();
        return payment;
    }

    [Fact]
    public async Task GetPayment_FirstCall_ReadsDatabaseAndCaches()
    {
        var payment = await SeedPaymentAsync();

        var result = await _handler.Handle(new GetPaymentQuery(payment.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Pending.ToString(), result.Value!.Status);

        // Entry must now be cached
        Assert.NotNull(await _cache.GetAsync<PaymentDto>(CacheKeys.PaymentStatus(payment.Id)));
    }

    [Fact]
    public async Task GetPayment_CacheHit_ServesCachedValue()
    {
        var payment = await SeedPaymentAsync();

        await _handler.Handle(new GetPaymentQuery(payment.Id), CancellationToken.None);

        // Change the database behind the cache
        payment.MarkSucceeded();
        await _context.SaveChangesAsync();

        // The cached (now stale) DTO must be served — proof the cache is used
        var cachedResult = await _handler.Handle(new GetPaymentQuery(payment.Id), CancellationToken.None);
        Assert.Equal(PaymentStatus.Pending.ToString(), cachedResult.Value!.Status);
    }

    [Fact]
    public async Task GetPayment_AfterInvalidation_ReadsFreshState()
    {
        var payment = await SeedPaymentAsync();

        await _handler.Handle(new GetPaymentQuery(payment.Id), CancellationToken.None);

        payment.MarkSucceeded();
        await _context.SaveChangesAsync();

        // Invalidation (what webhook handlers do after committing state changes)
        await _cache.RemoveAsync(CacheKeys.PaymentStatus(payment.Id));

        var freshResult = await _handler.Handle(new GetPaymentQuery(payment.Id), CancellationToken.None);
        Assert.Equal(PaymentStatus.Succeeded.ToString(), freshResult.Value!.Status);
    }
}
