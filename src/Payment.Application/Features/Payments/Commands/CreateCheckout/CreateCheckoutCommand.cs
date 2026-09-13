using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Payments.Commands.CreateCheckout;

public record CreateCheckoutCommand(
    Guid OrderId,
    string SuccessUrl,
    string CancelUrl
) : IRequest<Result<CreateCheckoutResponse>>;

public record CreateCheckoutResponse(
    Guid OrderId,
    Guid PaymentId,
    string CheckoutUrl,
    DateTime ExpiresAt
);
