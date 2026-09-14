using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Caching;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Entities;
using Payment.Domain.Enums;

namespace Payment.Application.Features.Payments.Commands.RefundPayment;

/// <summary>
/// Phase 9 — Refund initiation handler.
///
/// Follows the same failure-handling principles as Phase 7 checkout:
///  1. Validate and transition domain state FIRST, then commit.
///  2. Call the provider AFTER the commit (never hold a DB transaction open
///     during an external call).
///  3. On provider failure, COMPENSATE: refund -> Failed, payment back to
///     Succeeded, order back to Paid (the money is still captured).
///  4. Final refund confirmation arrives via the refund webhook — the payment
///     is not marked Refunded here.
/// </summary>
public class RefundPaymentCommandHandler : IRequestHandler<RefundPaymentCommand, Result<RefundPaymentResponse>>
{
    private readonly IAppDbContext _context;
    private readonly IPaymentProvider _paymentProvider;
    private readonly ILogger<RefundPaymentCommandHandler> _logger;
    private readonly IDistributedLock _distributedLock;
    private readonly ICacheService _cache;

    private static readonly TimeSpan RefundLockExpiry = TimeSpan.FromSeconds(30);

    public RefundPaymentCommandHandler(
        IAppDbContext context,
        IPaymentProvider paymentProvider,
        ILogger<RefundPaymentCommandHandler> logger)
        : this(context, paymentProvider, logger, new NoOpDistributedLock(), new NoOpCacheService())
    {
    }

    public RefundPaymentCommandHandler(
        IAppDbContext context,
        IPaymentProvider paymentProvider,
        ILogger<RefundPaymentCommandHandler> logger,
        IDistributedLock distributedLock,
        ICacheService cache)
    {
        _context = context;
        _paymentProvider = paymentProvider;
        _logger = logger;
        _distributedLock = distributedLock;
        _cache = cache;
    }

    public async Task<Result<RefundPaymentResponse>> Handle(
        RefundPaymentCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load payment + order
        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);

        if (payment is null)
            return Result<RefundPaymentResponse>.Failure($"Payment not found: {request.PaymentId}");

        // Phase 11: serialize concurrent refund requests for the same payment
        // (e.g. a double-clicked refund button). If the lock is unavailable we
        // proceed anyway — the state-machine guard below is authoritative:
        // a second request will see RefundProcessing and be rejected.
        var refundLock = await _distributedLock.TryAcquireAsync(
            CacheKeys.RefundLock(payment.Id), RefundLockExpiry, cancellationToken);

        try
        {
        // 2. Validate refund eligibility
        if (payment.Status != PaymentStatus.Succeeded)
            return Result<RefundPaymentResponse>.Failure(
                $"Only succeeded payments can be refunded. Current status: {payment.Status}");

        if (payment.Order.Status != OrderStatus.Paid)
            return Result<RefundPaymentResponse>.Failure(
                $"Only paid orders can be refunded. Current order status: {payment.Order.Status}");

        if (string.IsNullOrEmpty(payment.ProviderPaymentId))
            return Result<RefundPaymentResponse>.Failure(
                "Payment has no provider payment ID. Cannot refund.");

        // Refunds only allowed for the full amount (partial refunds out of scope)
        var refund = new Refund(payment.Id, payment.OrderId, payment.Amount, payment.Currency, request.Reason);

        // 3. Transition state and commit BEFORE calling the provider
        payment.StartRefund();
        payment.Order.StartRefund();
        _context.Refunds.Add(refund);
        await _context.SaveChangesAsync(cancellationToken);

        // Phase 11: cached payment read model is stale once state changes.
        await _cache.RemoveAsync(CacheKeys.PaymentStatus(payment.Id), cancellationToken);

        _logger.LogInformation(
            "Refund initiated. PaymentId={PaymentId}, RefundId={RefundId}, Amount={Amount} {Currency}",
            payment.Id, refund.Id, refund.Amount, refund.Currency);

        // 4. Call the provider OUTSIDE the DB transaction
        RefundResult providerResult;
        try
        {
            providerResult = await _paymentProvider.CreateRefundAsync(new RefundRequest
            {
                ProviderPaymentId = payment.ProviderPaymentId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                Reason = request.Reason
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // 5. Provider call failed -> compensate: money is still captured,
            //    so return payment/order to their pre-refund states.
            _logger.LogError(ex,
                "Provider refund call failed. Compensating. PaymentId={PaymentId}", payment.Id);

            refund.MarkFailed(ex.Message);
            payment.CancelRefund();
            payment.Order.CancelRefund();
            await _context.SaveChangesAsync(cancellationToken);

            await _cache.RemoveAsync(CacheKeys.PaymentStatus(payment.Id), cancellationToken);

            return Result<RefundPaymentResponse>.Failure($"Refund failed: {ex.Message}");
        }

        // 6. Provider accepted the refund. Final confirmation still comes via webhook.
        refund.SetProviderRefundId(providerResult.ProviderRefundId);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Refund accepted by provider. PaymentId={PaymentId}, ProviderRefundId={ProviderRefundId}, ProviderStatus={Status}",
            payment.Id, providerResult.ProviderRefundId, providerResult.Status);

        return Result<RefundPaymentResponse>.Success(new RefundPaymentResponse(
            refund.Id,
            payment.Id,
            providerResult.ProviderRefundId,
            payment.Status.ToString()));
        }
        finally
        {
            if (refundLock is not null)
                await refundLock.DisposeAsync();
        }
    }
}
