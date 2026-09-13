using MediatR;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Exceptions;

namespace Payment.Application.Features.Orders.Queries.GetOrder;

public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, Result<OrderDto>>
{
    private readonly IAppDbContext _context;

    public GetOrderQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<OrderDto>> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Course)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order is null)
            throw new NotFoundException(nameof(Domain.Entities.Order), request.OrderId);

        var dto = new OrderDto(
            order.Id,
            order.UserId,
            order.OrderNumber,
            order.Status.ToString(),
            order.Currency,
            order.Subtotal,
            order.Total,
            order.CreatedAt,
            order.UpdatedAt,
            order.Items.Select(i => new OrderItemDto(
                i.Id,
                i.CourseId,
                i.Course.Title,
                i.Quantity,
                i.UnitPrice,
                i.TotalPrice
            )).ToList()
        );

        return Result<OrderDto>.Success(dto);
    }
}
