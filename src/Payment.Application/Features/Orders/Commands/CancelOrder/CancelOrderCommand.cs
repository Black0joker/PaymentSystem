using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Orders.Commands.CancelOrder;

public record CancelOrderCommand(Guid OrderId) : IRequest<Result>;
