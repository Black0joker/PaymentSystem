using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Payments.Commands.CreatePayment;

public record CreatePaymentCommand(
    Guid OrderId,
    string Provider,
    string Currency = "USD"
) : IRequest<Result<Guid>>;
