using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Payments.Queries.ReconcilePayment;

public record ReconcilePaymentQuery(Guid PaymentId) : IRequest<Result<ReconciliationResult>>;

public record ReconciliationResult(
    Guid PaymentId,
    string LocalStatus,
    decimal LocalAmount,
    string LocalCurrency,
    string? ProviderStatus,
    decimal? ProviderAmount,
    string? ProviderCurrency,
    ReconciliationStatus ReconciliationStatus,
    string? Discrepancy,
    DateTime ReconciledAt
);

public enum ReconciliationStatus
{
    Matched,
    StatusMismatch,
    AmountMismatch,
    CurrencyMismatch,
    ProviderNotFound,
    LocalNotFound
}
