using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Orders.Commands.CreateOrder;

public record CreateOrderCommand(
    Guid UserId,
    List<CreateOrderItemDto> Items,
    string Currency = "USD"
) : IRequest<Result<Guid>>;

public record CreateOrderItemDto(
    Guid CourseId,
    int Quantity = 1
);
