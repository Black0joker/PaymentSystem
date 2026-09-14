using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Payment.Domain.Entities;
using Payment.Domain.Events;
using Payment.Infrastructure.Persistence;

namespace Payment.Infrastructure.Messaging.Consumers;

/// <summary>
/// Phase 12 — grants course access when an order is paid.
///
/// Idempotency layers (defense in depth):
///  1. Application check: skip courses the user already owns.
///  2. Database invariant: UNIQUE(UserId, CourseId) — the ACTUAL guarantee.
///     Duplicate deliveries racing past the check are rejected here and the
///     consumer treats the rejection as success (the enrollment already
///     exists, which is the desired end state).
/// </summary>
public class EnrollmentConsumer : IConsumer<OrderPaidEvent>
{
    private readonly AppDbContext _context;
    private readonly ILogger<EnrollmentConsumer> _logger;

    public EnrollmentConsumer(AppDbContext context, ILogger<EnrollmentConsumer> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderPaidEvent> context)
    {
        var evt = context.Message;

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == evt.OrderId, context.CancellationToken);

        if (order is null)
        {
            _logger.LogWarning(
                "EnrollmentConsumer: order {OrderId} not found. Skipping.", evt.OrderId);
            return;
        }

        var created = 0;

        foreach (var item in order.Items)
        {
            // Layer 1: application-level idempotency check
            var alreadyEnrolled = await _context.Enrollments
                .AnyAsync(e => e.UserId == evt.UserId && e.CourseId == item.CourseId,
                    context.CancellationToken);

            if (alreadyEnrolled)
            {
                _logger.LogInformation(
                    "EnrollmentConsumer: user {UserId} already enrolled in course {CourseId}. Skipping.",
                    evt.UserId, item.CourseId);
                continue;
            }

            _context.Enrollments.Add(new Enrollment(evt.UserId, item.CourseId, order.Id));
            created++;
        }

        try
        {
            await _context.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Layer 2: database-level idempotency. A concurrent delivery won
            // the race — the enrollment exists, which is exactly what we want.
            _logger.LogInformation(
                "EnrollmentConsumer: concurrent duplicate for order {OrderId} rejected by unique index (idempotent success).",
                evt.OrderId);
            return;
        }

        _logger.LogInformation(
            "EnrollmentConsumer: created {Count} enrollment(s) for order {OrderId}.",
            created, evt.OrderId);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // SQL Server unique constraint violation error numbers are 2627/2601
        return ex.InnerException?.Message.Contains("duplicate") == true ||
               ex.InnerException?.Message.Contains("unique") == true;
    }
}
