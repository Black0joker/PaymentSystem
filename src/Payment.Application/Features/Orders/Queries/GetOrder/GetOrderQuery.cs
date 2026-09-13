using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Orders.Queries.GetOrder;

public record GetOrderQuery(Guid OrderId) : IRequest<Result<OrderDto>>;

public record OrderDto(
    Guid Id,
    Guid UserId,
    string OrderNumber,
    string Status,
    string Currency,
    decimal Subtotal,
    decimal Total,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    List<OrderItemDto> Items
);

public record OrderItemDto(
    Guid Id,
    Guid CourseId,
    string CourseTitle,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice
);
