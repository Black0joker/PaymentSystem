using MediatR;
using Microsoft.AspNetCore.Mvc;
using Payment.Application.Features.Payments.Commands.CreateCheckout;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/checkout")]
public class CheckoutController : ControllerBase
{
    private readonly IMediator _mediator;

    public CheckoutController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Creates a checkout session for an order.
    /// Returns a URL the customer is redirected to for payment.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateCheckout(
        [FromBody] CreateCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateCheckoutCommand(
            request.OrderId,
            request.SuccessUrl,
            request.CancelUrl);

        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(new
        {
            orderId = result.Value!.OrderId,
            paymentId = result.Value.PaymentId,
            checkoutUrl = result.Value.CheckoutUrl,
            expiresAt = result.Value.ExpiresAt
        });
    }
}

public record CreateCheckoutRequest(
    Guid OrderId,
    string SuccessUrl,
    string CancelUrl
);
