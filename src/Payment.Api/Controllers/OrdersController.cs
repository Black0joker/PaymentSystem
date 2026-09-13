using MediatR;
using Microsoft.AspNetCore.Mvc;
using Payment.Application.Features.Orders.Commands.CancelOrder;
using Payment.Application.Features.Orders.Commands.CreateOrder;
using Payment.Application.Features.Orders.Queries.GetOrder;
using Payment.Application.Features.Orders.Queries.GetOrders;

namespace Payment.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderCommand command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return CreatedAtAction(nameof(GetOrder), new { id = result.Value }, new { orderId = result.Value });
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] Guid userId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrdersQuery(userId), cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrderQuery(id), cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(result.Value);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelOrder(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CancelOrderCommand(id), cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        return Ok(new { message = "Order cancelled successfully." });
    }
}
