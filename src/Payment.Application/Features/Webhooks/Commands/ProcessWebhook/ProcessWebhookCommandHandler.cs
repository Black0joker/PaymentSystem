using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Application.Features.Webhooks.Commands.ProcessWebhook;

public class ProcessWebhookCommandHandler : IRequestHandler<ProcessWebhookCommand, Result>
{
    private readonly IAppDbContext _context;
    private readonly IPaymentProvider _paymentProvider;
    private readonly ILogger<ProcessWebhookCommandHandler> _logger;

    public ProcessWebhookCommandHandler(
        IAppDbContext context,
        IPaymentProvider paymentProvider,
        ILogger<ProcessWebhookCommandHandler> logger)
    {
        _context = context;
        _paymentProvider = paymentProvider;
        _logger = logger;
    }

    public async Task<Result> Handle(ProcessWebhookCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify webhook signature — this MUST happen against the raw payload
        PaymentWebhookEvent webhookEvent;
        try
        {
            webhookEvent = _paymentProvider.VerifyWebhook(request.Payload, request.Signature);
        }
        catch (WebhookVerificationException ex)
        {
            _logger.LogWarning(ex, "Webhook signature verification failed.");
            return Result.Failure("Webhook signature verification failed.");
        }

        // 2. Idempotency check — has this event already been processed?
        var existingEvent = await _context.WebhookEvents
            .FirstOrDefaultAsync(e =>
                e.Provider == webhookEvent.Provider &&
                e.ProviderEventId == webhookEvent.EventId,
                cancellationToken);

        if (existingEvent != null)
        {
            _logger.LogInformation(
                "Duplicate webhook received. EventId={EventId}, already processed.",
                webhookEvent.EventId);
            return Result.Success(); // Return 200 — duplicate is not an error
        }

        // 3. Map provider event to our domain concept
        var mappedType = webhookEvent.GetMappedEventType();
        if (mappedType == MappedEventType.Unknown)
        {
            _logger.LogInformation(
                "Ignoring unknown webhook event type: {EventType}",
                webhookEvent.EventType);

            // Still record the event so we don't process it again
            var unknownEvent = new WebhookEvent(
                webhookEvent.Provider,
                webhookEvent.EventId,
                webhookEvent.EventType,
                request.Payload);
            unknownEvent.MarkProcessed();
            _context.WebhookEvents.Add(unknownEvent);
            await _context.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }

        // 4. Record the webhook event (insert will fail on unique constraint if race condition)
        var webhookEventEntity = new WebhookEvent(
            webhookEvent.Provider,
            webhookEvent.EventId,
            webhookEvent.EventType,
            request.Payload);

        _context.WebhookEvents.Add(webhookEventEntity);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Race condition: another request already inserted this event
            _logger.LogInformation(
                "Concurrent duplicate webhook detected. EventId={EventId}",
                webhookEvent.EventId);
            return Result.Success();
        }

        // 5. Process the event based on its type
        try
        {
            await ProcessEventAsync(webhookEvent, mappedType, cancellationToken);
            webhookEventEntity.MarkProcessed();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process webhook. EventId={EventId}, Type={Type}",
                webhookEvent.EventId, webhookEvent.EventType);

            webhookEventEntity.MarkFailed(ex.Message);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Failure($"Failed to process webhook: {ex.Message}");
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Webhook processed successfully. EventId={EventId}, Type={Type}",
            webhookEvent.EventId, webhookEvent.EventType);

        return Result.Success();
    }

    private async Task ProcessEventAsync(
        PaymentWebhookEvent webhookEvent,
        MappedEventType mappedType,
        CancellationToken cancellationToken)
    {
        switch (mappedType)
        {
            case MappedEventType.CheckoutCompleted:
            case MappedEventType.PaymentSucceeded:
                await HandlePaymentSucceededAsync(webhookEvent, cancellationToken);
                break;

            case MappedEventType.PaymentFailed:
                await HandlePaymentFailedAsync(webhookEvent, cancellationToken);
                break;

            case MappedEventType.PaymentRefunded:
                await HandlePaymentRefundedAsync(webhookEvent, cancellationToken);
                break;
        }
    }

    private async Task HandlePaymentSucceededAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken cancellationToken)
    {
        // Find payment by provider payment ID (session ID or payment intent ID)
        var providerId = webhookEvent.PaymentIntentId ?? webhookEvent.SessionId;
        if (string.IsNullOrEmpty(providerId))
        {
            throw new InvalidOperationException("Webhook event does not contain a payment identifier.");
        }

        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.ProviderPaymentId == providerId, cancellationToken);

        if (payment is null)
        {
            _logger.LogWarning(
                "Payment not found for provider ID: {ProviderId}", providerId);
            throw new InvalidOperationException($"Payment not found for provider ID: {providerId}");
        }

        // State machine: only transition if not already succeeded
        if (payment.Status == PaymentStatus.Succeeded)
        {
            _logger.LogInformation(
                "Payment already succeeded. PaymentId={PaymentId}", payment.Id);
            return;
        }

        // Update payment
        payment.MarkSucceeded();

        // Update order
        var order = payment.Order;
        if (order.Status == OrderStatus.PaymentProcessing)
        {
            order.MarkPaid();
        }

        _logger.LogInformation(
            "Payment succeeded. PaymentId={PaymentId}, OrderId={OrderId}",
            payment.Id, order.Id);
    }

    private async Task HandlePaymentFailedAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken cancellationToken)
    {
        var providerId = webhookEvent.PaymentIntentId;
        if (string.IsNullOrEmpty(providerId))
        {
            throw new InvalidOperationException("Webhook event does not contain a payment intent ID.");
        }

        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.ProviderPaymentId == providerId, cancellationToken);

        if (payment is null)
        {
            _logger.LogWarning(
                "Payment not found for provider ID: {ProviderId}", providerId);
            throw new InvalidOperationException($"Payment not found for provider ID: {providerId}");
        }

        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Failed)
        {
            _logger.LogInformation(
                "Payment already in terminal state. PaymentId={PaymentId}, Status={Status}",
                payment.Id, payment.Status);
            return;
        }

        payment.MarkFailed();

        var order = payment.Order;
        if (order.Status == OrderStatus.PaymentProcessing)
        {
            order.MarkFailed();
        }

        _logger.LogInformation(
            "Payment failed. PaymentId={PaymentId}, OrderId={OrderId}, Reason={Reason}",
            payment.Id, order.Id, webhookEvent.FailureReason);
    }

    private async Task HandlePaymentRefundedAsync(
        PaymentWebhookEvent webhookEvent,
        CancellationToken cancellationToken)
    {
        var providerId = webhookEvent.PaymentIntentId;
        if (string.IsNullOrEmpty(providerId))
        {
            throw new InvalidOperationException("Webhook event does not contain a payment intent ID.");
        }

        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.ProviderPaymentId == providerId, cancellationToken);

        if (payment is null)
        {
            _logger.LogWarning(
                "Payment not found for provider ID: {ProviderId}", providerId);
            throw new InvalidOperationException($"Payment not found for provider ID: {providerId}");
        }

        if (payment.Status != PaymentStatus.Succeeded)
        {
            _logger.LogWarning(
                "Cannot refund payment in state: {Status}. PaymentId={PaymentId}",
                payment.Status, payment.Id);
            return;
        }

        payment.MarkRefunded();

        _logger.LogInformation(
            "Payment refunded. PaymentId={PaymentId}, OrderId={OrderId}",
            payment.Id, payment.OrderId);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQL Server unique constraint violation error number is 2627 or 2601
        return ex.InnerException?.Message.Contains("duplicate") == true ||
               ex.InnerException?.Message.Contains("unique") == true;
    }
}
