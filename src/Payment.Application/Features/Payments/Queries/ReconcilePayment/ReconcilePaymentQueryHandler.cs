using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;

namespace Payment.Application.Features.Payments.Queries.ReconcilePayment;

public class ReconcilePaymentQueryHandler : IRequestHandler<ReconcilePaymentQuery, Result<ReconciliationResult>>
{
    private readonly IAppDbContext _context;
    private readonly IPaymentProvider _paymentProvider;
    private readonly ILogger<ReconcilePaymentQueryHandler> _logger;

    public ReconcilePaymentQueryHandler(
        IAppDbContext context,
        IPaymentProvider paymentProvider,
        ILogger<ReconcilePaymentQueryHandler> logger)
    {
        _context = context;
        _paymentProvider = paymentProvider;
        _logger = logger;
    }

    public async Task<Result<ReconciliationResult>> Handle(
        ReconcilePaymentQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Get local payment record
        var payment = await _context.Payments
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);

        if (payment is null)
        {
            return Result<ReconciliationResult>.Success(new ReconciliationResult(
                request.PaymentId,
                LocalStatus: "NotFound",
                LocalAmount: 0,
                LocalCurrency: string.Empty,
                ProviderStatus: null,
                ProviderAmount: null,
                ProviderCurrency: null,
                ReconciliationStatus: ReconciliationStatus.LocalNotFound,
                Discrepancy: "Payment not found in local database.",
                ReconciledAt: DateTime.UtcNow));
        }

        // 2. If no provider payment ID, we can't reconcile
        if (string.IsNullOrEmpty(payment.ProviderPaymentId))
        {
            return Result<ReconciliationResult>.Success(new ReconciliationResult(
                payment.Id,
                payment.Status.ToString(),
                payment.Amount,
                payment.Currency,
                ProviderStatus: null,
                ProviderAmount: null,
                ProviderCurrency: null,
                ReconciliationStatus: ReconciliationStatus.ProviderNotFound,
                Discrepancy: "Payment has no provider payment ID. Cannot reconcile.",
                ReconciledAt: DateTime.UtcNow));
        }

        // 3. Get provider payment details
        ProviderPaymentDetails? providerDetails;
        try
        {
            providerDetails = await _paymentProvider.GetPaymentAsync(
                payment.ProviderPaymentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to retrieve payment from provider. PaymentId={PaymentId}, ProviderPaymentId={ProviderPaymentId}",
                payment.Id, payment.ProviderPaymentId);

            return Result<ReconciliationResult>.Success(new ReconciliationResult(
                payment.Id,
                payment.Status.ToString(),
                payment.Amount,
                payment.Currency,
                ProviderStatus: null,
                ProviderAmount: null,
                ProviderCurrency: null,
                ReconciliationStatus: ReconciliationStatus.ProviderNotFound,
                Discrepancy: $"Failed to retrieve payment from provider: {ex.Message}",
                ReconciledAt: DateTime.UtcNow));
        }

        // 4. Compare local vs provider
        var (reconciliationStatus, discrepancy) = CompareRecords(payment, providerDetails);

        _logger.LogInformation(
            "Reconciliation completed. PaymentId={PaymentId}, Status={Status}",
            payment.Id, reconciliationStatus);

        return Result<ReconciliationResult>.Success(new ReconciliationResult(
            payment.Id,
            payment.Status.ToString(),
            payment.Amount,
            payment.Currency,
            providerDetails.Status,
            providerDetails.Amount,
            providerDetails.Currency,
            reconciliationStatus,
            discrepancy,
            DateTime.UtcNow));
    }

    private static (ReconciliationStatus Status, string? Discrepancy) CompareRecords(
        Domain.Entities.Payment payment,
        ProviderPaymentDetails providerDetails)
    {
        // Map provider status to our domain status
        var providerStatusMapped = MapProviderStatus(providerDetails.Status);

        // Check status match
        if (payment.Status.ToString() != providerStatusMapped)
        {
            return (ReconciliationStatus.StatusMismatch,
                $"Local status is '{payment.Status}' but provider status is '{providerDetails.Status}' (mapped: '{providerStatusMapped}').");
        }

        // Check amount match (allow small tolerance for floating point)
        if (Math.Abs(payment.Amount - providerDetails.Amount) > 0.01m)
        {
            return (ReconciliationStatus.AmountMismatch,
                $"Local amount is {payment.Amount} but provider amount is {providerDetails.Amount}.");
        }

        // Check currency match
        if (!string.Equals(payment.Currency, providerDetails.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return (ReconciliationStatus.CurrencyMismatch,
                $"Local currency is '{payment.Currency}' but provider currency is '{providerDetails.Currency}'.");
        }

        return (ReconciliationStatus.Matched, null);
    }

    private static string MapProviderStatus(string providerStatus)
    {
        return providerStatus.ToLowerInvariant() switch
        {
            "succeeded" => "Succeeded",
            "requires_payment_method" or "requires_confirmation" => "Processing",
            "canceled" => "Cancelled",
            "requires_action" => "Processing",
            _ => "Pending"
        };
    }
}
