using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Payment.Application.Abstractions.Caching;
using Payment.Application.Abstractions.Payments;
using Payment.Application.Abstractions.Persistence;
using Payment.Application.Common;
using Payment.Domain.Entities;
using Payment.Domain.Enums;
using Payment.Domain.Events;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Application.Features.Webhooks.Commands.ProcessWebhook;

public class ProcessWebhookCommandHandler : IRequestHandler<ProcessWebhookCommand, Result>
{
    private readonly IAppDbContext _context;
    private readonly IPaymentProvider _paymentProvider;
    private readonly ILogger<ProcessWebhookCommandHandler> _logger;
    private readonly ICacheService _cache;
    private readonly IDistributedLock _distributedLock;

    private static readonly TimeSpan WebhookProcessedTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan WebhookLockExpiry = TimeSpan.FromSeconds(30);

    public ProcessWebhookCommandHandler(
        IAppDbContext context,
        IPaymentProvider paymentProvider,
        ILogger<ProcessWebhookCommandHandler> logger)
        : this(context, paymentProvider, logger, new NoOpCacheService(), new NoOpDistributedLock())
    {
    }

    public ProcessWebhookCommandHandler(
        IAppDbContext context,
        IPaymentProvider paymentProvider,
        ILogger<ProcessWebhookCommandHandler> logger,
        ICacheService cache,
        IDistributedLock distributedLock)
    {
        _context = context;
        _paymentProvider = paymentProvider;
        _logger = logger;
        _cache = cache;
        _distributedLock = distributedLock;
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

        // Phase 11 optimization: fast-path idempotency via the cache layer.
        // The marker is written only after a fully committed successful
        // processing, so a hit is always safe. Misses or cache failures fall
        // through to the authoritative database check below.
        if (await _cache.GetAsync<bool>(
                CacheKeys.WebhookProcessed(webhookEvent.Provider, webhookEvent.EventId),
                cancellationToken))
        {
            _logger.LogInformation(
                "Duplicate webhook rejected by cache fast path. EventId={EventId}",
                webhookEvent.EventId);
            return Result.Success();
        }

        // Phase 11: serialize concurrent webhooks targeting the SAME payment.
        // Both Busy and Unavailable fall through — unique constraints on
        // WebhookEvents and state-machine guards remain the correctness
        // mechanism (duplicate deliveries are idempotent).
        var webhookLockKey = webhookEvent.PaymentIntentId ?? webhookEvent.SessionId;
        IAsyncDisposable? webhookLock = null;
        if (!string.IsNullOrEmpty(webhookLockKey))
        {
            var webhookLockResult = await _distributedLock.TryAcquireAsync(
                CacheKeys.WebhookPaymentLock(webhookLockKey), WebhookLockExpiry, cancellationToken);
            webhookLock = webhookLockResult.Handle;
        }

        try
        {
        // 2. Idempotency check — has this event already been processed?
        var existingEvent = await _context.WebhookEvents
            .FirstOrDefaultAsync(e =>
                e.Provider == webhookEvent.Provider &&
                e.ProviderEventId == webhookEvent.EventId,
                cancellationToken);

        WebhookEvent webhookEventEntity;

        if (existingEvent != null)
        {
            if (existingEvent.Status == WebhookEventStatus.Processed)
            {
                _logger.LogInformation(
                    "Duplicate webhook received. EventId={EventId} already processed.",
                    webhookEvent.EventId);
                return Result.Success(); // Return 200 — duplicate is not an error
            }

            // Reprocess when the event is not fully processed:
            //  - Failed:   a previous attempt errored; the provider is retrying.
            //  - Received: inserted but the final commit never happened
            //              (e.g. crash during commit — see Phase 7).
            // Reprocessing is safe: every event handler is idempotent via the
            // domain state-machine guards.
            _logger.LogInformation(
                "Reprocessing unfinished webhook. EventId={EventId}, Status={Status}",
                webhookEvent.EventId, existingEvent.Status);
            existingEvent.MarkReceivedForRetry();
            webhookEventEntity = existingEvent;
        }
        else
        {
            // 3. Record the webhook event (insert fails on unique constraint if race condition)
            webhookEventEntity = new WebhookEvent(
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
        }

        // 4. Map provider event to our domain concept
        var mappedType = webhookEvent.GetMappedEventType();
        if (mappedType == MappedEventType.Unknown)
        {
            _logger.LogInformation(
                "Ignoring unknown webhook event type: {EventType}",
                webhookEvent.EventType);

            webhookEventEntity.MarkProcessed();
            await _context.SaveChangesAsync(cancellationToken);
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

            // Discard any partial domain/outbox changes made during processing so a
            // failed attempt leaves Payment/Order exactly as they were (atomicity).
            foreach (var entry in _context.ChangeTracker.Entries()
                .Where(e => !ReferenceEquals(e.Entity, webhookEventEntity)).ToList())
            {
                if (entry.State == EntityState.Modified)
                    entry.State = EntityState.Unchanged;
                else if (entry.State == EntityState.Added)
                    entry.State = EntityState.Detached;
            }

            // Record the failure so the provider's retry can reprocess this event.
            webhookEventEntity.MarkFailed(ex.Message);
            await _context.SaveChangesAsync(cancellationToken);
            return Result.Failure($"Failed to process webhook: {ex.Message}");
        }

        // 6. Single atomic commit: webhook event + payment + order + outbox messages
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Webhook processed successfully. EventId={EventId}, Type={Type}",
            webhookEvent.EventId, webhookEvent.EventType);

        // Phase 11: remember successful processing so duplicate deliveries
        // are rejected by the cache layer without touching the database.
        await _cache.SetAsync(
            CacheKeys.WebhookProcessed(webhookEvent.Provider, webhookEvent.EventId),
            true, WebhookProcessedTtl, cancellationToken);

        return Result.Success();
        }
        finally
        {
            if (webhookLock is not null)
                await webhookLock.DisposeAsync();
        }
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
        // Find payment by provider payment ID (session ID or payment intent ID).
        // checkout.session.completed must be matched by SESSION ID first:
        // checkout stores the session ID, and Stripe sends both IDs in that event,
        // so a PaymentIntent-first lookup would never find the payment.
        if (string.IsNullOrEmpty(webhookEvent.PaymentIntentId) && string.IsNullOrEmpty(webhookEvent.SessionId))
        {
            throw new InvalidOperationException("Webhook event does not contain a payment identifier.");
        }

        Payment.Domain.Entities.Payment? payment = null;

        if (!string.IsNullOrEmpty(webhookEvent.SessionId))
        {
            var sessionId = webhookEvent.SessionId;
            payment = await _context.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.ProviderPaymentId == sessionId, cancellationToken);
        }

        if (payment is null && !string.IsNullOrEmpty(webhookEvent.PaymentIntentId))
        {
            var intentId = webhookEvent.PaymentIntentId;
            payment = await _context.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.ProviderPaymentId == intentId, cancellationToken);
        }

        if (payment is null)
        {
            _logger.LogWarning(
                "Payment not found for webhook. SessionId={SessionId}, PaymentIntentId={PaymentIntentId}",
                webhookEvent.SessionId, webhookEvent.PaymentIntentId);
            throw new InvalidOperationException(
                $"Payment not found. SessionId={webhookEvent.SessionId}, PaymentIntentId={webhookEvent.PaymentIntentId}");
        }

        // Real-flow fix: checkout stores the SESSION ID as ProviderPaymentId.
        // Upgrade it to the PaymentIntent ID once known so subsequent webhooks
        // (payment_intent.*) and provider reconciliation can locate the payment.
        if (!string.IsNullOrEmpty(webhookEvent.PaymentIntentId) &&
            payment.ProviderPaymentId != webhookEvent.PaymentIntentId)
        {
            payment.SetProviderPaymentId(webhookEvent.PaymentIntentId);
        }

        // State machine + idempotency: already-succeeded is a no-op, not an error.
        if (payment.Status == PaymentStatus.Succeeded)
        {
            _logger.LogInformation(
                "Payment already succeeded. PaymentId={PaymentId}", payment.Id);
            return;
        }

        // Update payment
        payment.MarkSucceeded();

        // Phase 11: invalidate the cached payment read model (state changed).
        await _cache.RemoveAsync(CacheKeys.PaymentStatus(payment.Id), cancellationToken);

        // Update order
        var order = payment.Order;
        if (order.Status == OrderStatus.PaymentProcessing)
        {
            order.MarkPaid();
        }

        // Outbox: enqueue domain events in the SAME transactional unit.
        // If this save commits, the events are guaranteed to be published later.
        // If anything fails before commit, no orphan events are published.
        _context.OutboxMessages.Add(new OutboxMessage(
            nameof(PaymentSucceededEvent),
            JsonSerializer.Serialize(new PaymentSucceededEvent(
                payment.Id, payment.OrderId, payment.ProviderPaymentId!,
                payment.Amount, payment.Currency, DateTime.UtcNow))));

        _context.OutboxMessages.Add(new OutboxMessage(
            nameof(OrderPaidEvent),
            JsonSerializer.Serialize(new OrderPaidEvent(
                order.Id, order.UserId, payment.Id,
                order.Total, order.Currency, DateTime.UtcNow))));

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

        // Out-of-order protection: if the payment already reached a terminal state
        // (e.g. a 'failed' webhook arrives AFTER a 'succeeded' one), treat as no-op.
        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Failed)
        {
            _logger.LogInformation(
                "Payment already in terminal state. PaymentId={PaymentId}, Status={Status}",
                payment.Id, payment.Status);
            return;
        }

        payment.MarkFailed();

        // Phase 11: invalidate the cached payment read model (state changed).
        await _cache.RemoveAsync(CacheKeys.PaymentStatus(payment.Id), cancellationToken);

        var order = payment.Order;
        if (order.Status == OrderStatus.PaymentProcessing)
        {
            order.MarkFailed();
        }

        _context.OutboxMessages.Add(new OutboxMessage(
            nameof(PaymentFailedEvent),
            JsonSerializer.Serialize(new PaymentFailedEvent(
                payment.Id, payment.OrderId, webhookEvent.FailureReason, DateTime.UtcNow))));

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

        // Idempotency: already refunded -> no-op.
        if (payment.Status == PaymentStatus.Refunded)
        {
            _logger.LogInformation(
                "Payment already refunded. PaymentId={PaymentId}", payment.Id);
            return;
        }

        // Refunds can only be confirmed from RefundProcessing (our refund flow) or
        // Succeeded (refunds issued outside our system, e.g. provider dashboard).
        // Any other state means events arrived out of order -> throw so the event
        // is recorded Failed and reprocessed when the provider retries.
        if (payment.Status is not (PaymentStatus.RefundProcessing or PaymentStatus.Succeeded))
        {
            _logger.LogWarning(
                "Cannot refund payment in state: {Status}. PaymentId={PaymentId}",
                payment.Status, payment.Id);
            throw new InvalidOperationException(
                $"Cannot refund payment in state: {payment.Status}");
        }

        var order = payment.Order;

        payment.MarkRefunded();

        // Phase 11: invalidate the cached payment read model (state changed).
        await _cache.RemoveAsync(CacheKeys.PaymentStatus(payment.Id), cancellationToken);

        // Order transitions: RefundProcessing -> Refunded (our flow).
        // Dashboard refunds arrive while the order is still Paid -> walk it through.
        if (order.Status == OrderStatus.RefundProcessing)
        {
            order.MarkRefunded();
        }
        else if (order.Status == OrderStatus.Paid)
        {
            order.StartRefund();
            order.MarkRefunded();
        }

        // Confirm the matching refund record, if one exists
        var refund = !string.IsNullOrEmpty(webhookEvent.RefundId)
            ? await _context.Refunds.FirstOrDefaultAsync(
                r => r.ProviderRefundId == webhookEvent.RefundId, cancellationToken)
            : await _context.Refunds
                .Where(r => r.PaymentId == payment.Id && r.Status == RefundStatus.Pending)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

        refund?.MarkSucceeded();

        // Outbox: publish OrderRefundedEvent in the same transactional unit
        _context.OutboxMessages.Add(new OutboxMessage(
            nameof(OrderRefundedEvent),
            JsonSerializer.Serialize(new OrderRefundedEvent(
                order.Id, payment.Id, payment.Amount, payment.Currency, DateTime.UtcNow))));

        _logger.LogInformation(
            "Payment refunded. PaymentId={PaymentId}, OrderId={OrderId}",
            payment.Id, order.Id);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQL Server unique constraint violation error number is 2627 or 2601
        return ex.InnerException?.Message.Contains("duplicate") == true ||
               ex.InnerException?.Message.Contains("unique") == true;
    }
}
