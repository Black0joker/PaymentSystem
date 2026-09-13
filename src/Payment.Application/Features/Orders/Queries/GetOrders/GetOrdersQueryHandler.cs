using MediatR;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;

namespace Payment.Application.Features.Orders.Queries.GetOrders;

public class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, Result<List<OrderSummaryDto>>>
{
    private readonly IAppDbContext _context;

    public GetOrdersQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<OrderSummaryDto>>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await _context.Orders
            .Include(o => o.Items)
            .Where(o => o.UserId == request.UserId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.OrderNumber,
                o.Status.ToString(),
                o.Currency,
                o.Total,
                o.CreatedAt,
                o.Items.Count
            ))
            .ToListAsync(cancellationToken);

        return Result<List<OrderSummaryDto>>.Success(orders);
    }
}
