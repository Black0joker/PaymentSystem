using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payment.Application.Abstractions.Payments;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Domain.Events;
using Payment.Infrastructure.Persistence;

namespace Payment.Infrastructure.Reconciliation;

/// <summary>
/// Phase 10 — Payment reconciliation worker.
///
/// Webhooks can be lost, delayed or arrive out of order. This worker is the
/// repair net: it periodically finds payments stuck in Pending/Processing,
/// asks the payment provider for the authoritative state, and corrects local
/// records that drifted from reality.
///
/// Repair rules (local -> provider):
///   Pending/Processing -> succeeded : MarkSucceeded (+ order Paid + outbox events)
///   Pending/Processing -> canceled  : Cancel (+ order Failed + outbox event)
///   Pending/Processing -> still in flight : no action (provider still working)
///
/// Safety: payments without a provider ID and payments younger than the
/// stuck threshold are never touched. Every payment is repaired in its own
/// save, and individual failures never stop the batch.
/// </summary>
public class PaymentReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPaymentProvider _paymentProvider;
    private readonly ReconciliationOptions _options;
    private readonly ILogger<PaymentReconciliationWorker> _logger;

    public PaymentReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IPaymentProvider paymentProvider,
        IOptions<ReconciliationOptions> options,
        ILogger<PaymentReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _paymentProvider = paymentProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Reconciliation worker started. Interval={Interval}s, Threshold={Threshold}min, BatchSize={Batch}",
            _options.IntervalSeconds, _options.StuckThresholdMinutes, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The worker must never crash the host — log and continue polling.
                _logger.LogError(ex, "Reconciliation cycle failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Reconciles one batch of stuck payments. Public so it can be invoked
    /// directly in tests without running the full background loop.
    /// </summary>
    public async Task ReconcileBatchAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow.AddMinutes(-_options.StuckThresholdMinutes);

        // Stuck = Pending/Processing, older than the threshold, and has a
        // provider ID we can ask about.
        var stuckPayments = await context.Payments
            .Include(p => p.Order)
            .Where(p =>
                (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing) &&
                p.ProviderPaymentId != null &&
                (p.UpdatedAt ?? p.CreatedAt) <= cutoff)
            .OrderBy(p => p.UpdatedAt ?? p.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (stuckPayments.Count == 0)
            return;

        _logger.LogInformation("Reconciling {Count} stuck payment(s).", stuckPayments.Count);

        foreach (var payment in stuckPayments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ReconcilePaymentAsync(context, payment, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Isolation: one bad payment must never stop the rest of the batch.
                _logger.LogError(ex,
                    "Failed to reconcile payment. PaymentId={PaymentId}, ProviderPaymentId={ProviderPaymentId}",
                    payment.Id, payment.ProviderPaymentId);
            }
        }
    }

    private async Task ReconcilePaymentAsync(
        AppDbContext context,
        Payment.Domain.Entities.Payment payment,
        CancellationToken cancellationToken)
    {
        var details = await _paymentProvider.GetPaymentAsync(payment.ProviderPaymentId!, cancellationToken);

        switch (details.Status?.ToLowerInvariant())
        {
            case "succeeded":
                // Provider captured the money — bring local state in line.
                payment.MarkSucceeded();

                if (payment.Order.Status == OrderStatus.PaymentProcessing)
                {
                    payment.Order.MarkPaid();
                }

                context.OutboxMessages.Add(new OutboxMessage(
                    nameof(PaymentSucceededEvent),
                    JsonSerializer.Serialize(new PaymentSucceededEvent(
                        payment.Id, payment.OrderId, payment.ProviderPaymentId!,
                        payment.Amount, payment.Currency, DateTime.UtcNow))));

                context.OutboxMessages.Add(new OutboxMessage(
                    nameof(OrderPaidEvent),
                    JsonSerializer.Serialize(new OrderPaidEvent(
                        payment.Order.Id, payment.Order.UserId, payment.Id,
                        payment.Order.Total, payment.Order.Currency, DateTime.UtcNow))));

                await context.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Reconciliation repaired payment. PaymentId={PaymentId}, ProviderPaymentId={ProviderPaymentId}, LocalStatus was Pending/Processing, provider says succeeded.",
                    payment.Id, payment.ProviderPaymentId);
                break;

            case "canceled":
            case "cancelled":
                // Provider will never capture this payment — close it locally.
                payment.Cancel();

                if (payment.Order.Status == OrderStatus.PaymentProcessing)
                {
                    payment.Order.MarkFailed();
                }

                context.OutboxMessages.Add(new OutboxMessage(
                    nameof(PaymentFailedEvent),
                    JsonSerializer.Serialize(new PaymentFailedEvent(
                        payment.Id, payment.OrderId,
                        "Payment canceled at provider (reconciliation)", DateTime.UtcNow))));

                await context.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Reconciliation repaired payment. PaymentId={PaymentId}, provider says canceled.",
                    payment.Id);
                break;

            default:
                // Still genuinely in flight (requires_action, processing, ...) —
                // no repair needed; check again next cycle.
                _logger.LogDebug(
                    "Payment still in flight at provider. PaymentId={PaymentId}, ProviderStatus={Status}",
                    payment.Id, details.Status);
                break;
        }
    }
}
