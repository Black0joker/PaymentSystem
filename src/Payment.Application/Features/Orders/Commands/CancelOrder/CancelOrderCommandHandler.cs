using MediatR;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Exceptions;

namespace Payment.Application.Features.Orders.Commands.CancelOrder;

public class CancelOrderCommandHandler : IRequestHandler<CancelOrderCommand, Result>
{
    private readonly IAppDbContext _context;

    public CancelOrderCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order is null)
            throw new NotFoundException(nameof(Domain.Entities.Order), request.OrderId);

        try
        {
            order.Cancel();
        }
        catch (InvalidStateTransitionException ex)
        {
            return Result.Failure(ex.Message);
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
