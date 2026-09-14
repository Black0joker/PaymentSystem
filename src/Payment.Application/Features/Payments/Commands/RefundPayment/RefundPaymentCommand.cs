using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Payments.Commands.RefundPayment;

/// <summary>
/// Phase 9 — Initiates a refund for a succeeded payment.
/// Flow: validate -> payment RefundProcessing + order RefundProcessing ->
/// call provider -> provider webhook confirms -> Refunded.
/// </summary>
public record RefundPaymentCommand(
    Guid PaymentId,
    string? Reason
) : IRequest<Result<RefundPaymentResponse>>;

public record RefundPaymentResponse(
    Guid RefundId,
    Guid PaymentId,
    string? ProviderRefundId,
    string Status
);
