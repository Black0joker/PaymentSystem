using MediatR;
using Microsoft.AspNetCore.Mvc;
using Payment.Application.Features.Payments.Commands.CreatePayment;
using Payment.Application.Features.Payments.Queries.GetPayment;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(GetPayment), new { id = result.Value }, new { paymentId = result.Value });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetPayment(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetPaymentQuery(id), cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }
}
