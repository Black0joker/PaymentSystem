using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Enums;
using Payment.Domain.Exceptions;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Application.Features.Payments.Commands.CreateCheckout;

public class CreateCheckoutCommandHandler : IRequestHandler<CreateCheckoutCommand, Result<CreateCheckoutResponse>>
{
    private readonly IAppDbContext _context;
    private readonly IPaymentProvider _paymentProvider;
    private readonly ILogger<CreateCheckoutCommandHandler> _logger;

    public CreateCheckoutCommandHandler(
        IAppDbContext context,
        IPaymentProvider paymentProvider,
        ILogger<CreateCheckoutCommandHandler> logger)
    {
        _context = context;
        _paymentProvider = paymentProvider;
        _logger = logger;
    }

    public async Task<Result<CreateCheckoutResponse>> Handle(
        CreateCheckoutCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Load order with items
        var order = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Course)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, cancellationToken);

        if (order is null)
            throw new NotFoundException(nameof(Domain.Entities.Order), request.OrderId);

        // 2. Validate order state
        if (order.Status != OrderStatus.Pending)
            return Result<CreateCheckoutResponse>.Failure(
                $"Order is in '{order.Status}' state. Only 'Pending' orders can create checkout.");

        if (!order.Items.Any())
            return Result<CreateCheckoutResponse>.Failure("Order has no items.");

        if (order.Total <= 0)
            return Result<CreateCheckoutResponse>.Failure("Order total must be greater than zero.");

        // 3. Transition order to PaymentProcessing
        order.MarkPaymentProcessing();

        // 4. Create Payment entity (before calling provider — commit first)
        var payment = new PaymentEntity(order.Id, "Stripe", order.Total, order.Currency);

        // 5. Create first payment attempt
        var attempt = new Domain.Entities.PaymentAttempt(
            payment.Id, "Stripe", order.Total, order.Currency, attemptNumber: 1);

        _context.Payments.Add(payment);
        _context.PaymentAttempts.Add(attempt);
        await _context.SaveChangesAsync(cancellationToken);

        // 6. Call payment provider OUTSIDE the database transaction
        // This is the correct pattern: commit DB first, then call external service
        try
        {
            var checkoutRequest = new CheckoutRequest
            {
                OrderId = order.Id.ToString(),
                OrderNumber = order.OrderNumber,
                Amount = order.Total,
                Currency = order.Currency,
                CustomerEmail = order.User.Email,
                SuccessUrl = request.SuccessUrl,
                CancelUrl = request.CancelUrl,
                LineItems = order.Items.Select(i => new CheckoutLineItem
                {
                    Name = i.Course.Title,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity
                }).ToList()
            };

            var sessionResult = await _paymentProvider.CreateCheckoutSessionAsync(
                checkoutRequest, cancellationToken);

            // 7. Update payment with provider session ID
            payment.SetProviderPaymentId(sessionResult.SessionId);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Checkout created. OrderId={OrderId}, PaymentId={PaymentId}, SessionId={SessionId}",
                order.Id, payment.Id, sessionResult.SessionId);

            return Result<CreateCheckoutResponse>.Success(new CreateCheckoutResponse(
                order.Id,
                payment.Id,
                sessionResult.CheckoutUrl,
                sessionResult.ExpiresAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create checkout session. OrderId={OrderId}, PaymentId={PaymentId}",
                order.Id, payment.Id);

            // Revert order status since provider call failed
            // In production, you'd use a background worker to handle this
            return Result<CreateCheckoutResponse>.Failure(
                "Failed to create checkout session with payment provider.");
        }
    }
}
