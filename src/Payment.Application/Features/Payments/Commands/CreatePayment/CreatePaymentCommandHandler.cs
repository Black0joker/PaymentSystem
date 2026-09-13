using MediatR;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Entities;
using PaymentEntity = Payment.Domain.Entities.Payment;
using Payment.Domain.Enums;
using Payment.Domain.Exceptions;

namespace Payment.Application.Features.Payments.Commands.CreatePayment;

public class CreatePaymentCommandHandler : IRequestHandler<CreatePaymentCommand, Result<Guid>>
{
    private readonly IAppDbContext _context;

    public CreatePaymentCommandHandler(IAppDbContext context)
    {
        _context = context;
    }

    public async Task<Result<Guid>> Handle(CreatePaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order is null)
            throw new NotFoundException(nameof(Order), request.OrderId);

        if (order.Status != OrderStatus.Pending)
            return Result<Guid>.Failure($"Order is in '{order.Status}' state. Only 'Pending' orders can initiate payment.");

        if (order.Total <= 0)
            return Result<Guid>.Failure("Order total must be greater than zero.");

        // Transition order to PaymentProcessing
        order.MarkPaymentProcessing();

        // Create payment record
        var payment = new PaymentEntity(order.Id, request.Provider, order.Total, request.Currency);

        // Create first payment attempt
        var attempt = new PaymentAttempt(
            payment.Id,
            request.Provider,
            order.Total,
            request.Currency,
            attemptNumber: 1
        );

        _context.Payments.Add(payment);
        _context.PaymentAttempts.Add(attempt);

        await _context.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(payment.Id);
    }
}
