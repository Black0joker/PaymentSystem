using MediatR;
using Microsoft.AspNetCore.Mvc;
using Payment.Application.Features.Payments.Commands.CreatePayment;
using Payment.Application.Features.Payments.Queries.GetPayment;
using Payment.Application.Features.Payments.Queries.ReconcilePayment;

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

    /// <summary>
    /// Reconciles local payment records with the payment provider.
    /// Compares status, amount, and currency to detect discrepancies.
    /// </summary>
    [HttpGet("{id}/reconcile")]
    public async Task<IActionResult> ReconcilePayment(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ReconcilePaymentQuery(id), cancellationToken);

        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });

        var reconciliation = result.Value!;

        return Ok(new
        {
            paymentId = reconciliation.PaymentId,
            localStatus = reconciliation.LocalStatus,
            localAmount = reconciliation.LocalAmount,
            localCurrency = reconciliation.LocalCurrency,
            providerStatus = reconciliation.ProviderStatus,
            providerAmount = reconciliation.ProviderAmount,
            providerCurrency = reconciliation.ProviderCurrency,
            reconciliationStatus = reconciliation.ReconciliationStatus.ToString(),
            discrepancy = reconciliation.Discrepancy,
            reconciledAt = reconciliation.ReconciledAt
        });
    }
}
