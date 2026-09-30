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

    /// <summary>
    /// Initiates a refund for a succeeded payment.
    /// Returns 202 Accepted — final confirmation arrives via refund webhook.
    /// </summary>
    [HttpPost("{id}/refund")]
    public async Task<IActionResult> RefundPayment(Guid id, [FromBody] RefundPaymentRequest? request, CancellationToken cancellationToken)
    {
        var command = new Payment.Application.Features.Payments.Commands.RefundPayment.RefundPaymentCommand(id, request?.Reason);
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            // Concurrent refund for the same payment -> 409, not 400.
            if (result.Error?.Contains("already in progress", StringComparison.OrdinalIgnoreCase) == true)
                return Conflict(new { error = result.Error });

            return BadRequest(new { error = result.Error });
        }

        var value = result.Value!;
        return Accepted(new
        {
            refundId = value.RefundId,
            paymentId = value.PaymentId,
            providerRefundId = value.ProviderRefundId,
            status = value.Status
        });
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
