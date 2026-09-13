using MediatR;
using Payment.Application.Common;

namespace Payment.Application.Features.Orders.Queries.GetOrders;

public record GetOrdersQuery(Guid UserId) : IRequest<Result<List<OrderSummaryDto>>>;

public record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    string Status,
    string Currency,
    decimal Total,
    DateTime CreatedAt,
    int ItemCount
);
