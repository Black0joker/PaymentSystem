using MediatR;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Caching;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Exceptions;

namespace Payment.Application.Features.Payments.Queries.GetPayment;

public class GetPaymentQueryHandler : IRequestHandler<GetPaymentQuery, Result<PaymentDto>>
{
    /// <summary>
    /// Payment status reads are cached only briefly. Financial state lives in
    /// SQL Server; the cache just absorbs repeated polling (e.g. a client
    /// waiting for the webhook to land). Invalidation happens whenever a
    /// webhook or refund commits a state change.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private readonly IAppDbContext _context;
    private readonly ICacheService _cache;

    public GetPaymentQueryHandler(IAppDbContext context)
        : this(context, new NoOpCacheService())
    {
    }

    public GetPaymentQueryHandler(IAppDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Result<PaymentDto>> Handle(GetPaymentQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.PaymentStatus(request.PaymentId);

        // Cache-aside: serve hot reads from Redis; cold reads hit SQL and
        // refill the cache. Any cache failure falls through to the database.
        var cached = await _cache.GetAsync<PaymentDto>(cacheKey, cancellationToken);
        if (cached is not null)
            return Result<PaymentDto>.Success(cached);

        var payment = await _context.Payments
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);

        if (payment is null)
            throw new NotFoundException("Payment", request.PaymentId);

        var dto = new PaymentDto(
            payment.Id,
            payment.OrderId,
            payment.Provider,
            payment.ProviderPaymentId,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.CreatedAt,
            payment.UpdatedAt,
            payment.Attempts
                .OrderBy(a => a.AttemptNumber)
                .Select(a => new PaymentAttemptDto(
                    a.Id,
                    a.AttemptNumber,
                    a.Status.ToString(),
                    a.ProviderPaymentId,
                    a.Amount,
                    a.CompletedAt,
                    a.FailureReason
                )).ToList()
        );

        await _cache.SetAsync(cacheKey, dto, CacheTtl, cancellationToken);

        return Result<PaymentDto>.Success(dto);
    }
}
