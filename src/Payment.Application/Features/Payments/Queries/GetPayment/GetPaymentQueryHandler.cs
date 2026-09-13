using MediatR;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Exceptions;

namespace Payment.Application.Features.Payments.Queries.GetPayment;

public class GetPaymentQueryHandler : IRequestHandler<GetPaymentQuery, Result<PaymentDto>>
{
    private readonly IAppDbContext _context;

    public GetPaymentQueryHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PaymentDto>> Handle(GetPaymentQuery request, CancellationToken cancellationToken)
    {
        var payment = await _context.Payments
            .Include(p => p.Attempts)
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, cancellationToken);

        if (payment is null)
            throw new NotFoundException("Payment", request.PaymentId);

        var dto = new PaymentDto(
            payment.Id,
            payment.OrderId,
            payment.Provider,
            payment.ProviderPaymentId,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.CreatedAt,
            payment.UpdatedAt,
            payment.Attempts
                .OrderBy(a => a.AttemptNumber)
                .Select(a => new PaymentAttemptDto(
                    a.Id,
                    a.AttemptNumber,
                    a.Status.ToString(),
                    a.ProviderPaymentId,
                    a.Amount,
                    a.CompletedAt,
                    a.FailureReason
                )).ToList()
        );

        return Result<PaymentDto>.Success(dto);
    }
}
