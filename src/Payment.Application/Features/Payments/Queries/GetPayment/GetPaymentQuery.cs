using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Payments.Queries.GetPayment;

public record GetPaymentQuery(Guid PaymentId) : IRequest<Result<PaymentDto>>;

public record PaymentDto(
    Guid Id,
    Guid OrderId,
    string Provider,
    string? ProviderPaymentId,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<PaymentAttemptDto> Attempts
);

public record PaymentAttemptDto(
    Guid Id,
    int AttemptNumber,
    string Status,
    string? ProviderPaymentId,
    decimal Amount,
    DateTime? CompletedAt,
    string? FailureReason
);
