using MediatR;
using Microsoft.AspNetCore.Mvc;
using Payment.Application.Features.Webhooks.Commands.ProcessWebhook;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IMediator _mediator;

    public WebhooksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Receives webhook events from Stripe.
    /// The raw body is read for signature verification — do not use [FromBody] deserialization.
    /// </summary>
    [HttpPost("stripe")]
    public async Task<IActionResult> HandleStripeWebhook(CancellationToken cancellationToken)
    {
        // Read the raw request body — signature verification requires the exact bytes
        using var reader = new StreamReader(HttpContext.Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        // Get the signature header
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();

        if (string.IsNullOrEmpty(payload))
            return BadRequest(new { error = "Empty payload." });

        if (string.IsNullOrEmpty(signature))
            return BadRequest(new { error = "Missing Stripe-Signature header." });

        var result = await _mediator.Send(
            new ProcessWebhookCommand(payload, signature),
            cancellationToken);

        if (!result.IsSuccess)
        {
            // Return 400 for signature failures, 200 for duplicates
            // The handler distinguishes between these cases
            if (result.Error?.Contains("signature") == true)
                return BadRequest(new { error = result.Error });

            // Processing failures still return 200 to prevent provider retries
            // The event is recorded as Failed and can be retried manually
            return Ok(new { message = "Event recorded but processing failed.", error = result.Error });
        }

        return Ok(new { message = "Webhook processed successfully." });
    }
}
