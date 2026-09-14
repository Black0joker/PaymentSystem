using Payment.Domain.Common;

namespace Payment.Domain.Entities;

/// <summary>
/// Phase 12 — course access granted by a paid order.
///
/// Created by the enrollment consumer when an OrderPaidEvent arrives.
///
/// Idempotency invariant: UNIQUE(UserId, CourseId) at the database level.
/// Duplicate OrderPaidEvent deliveries therefore can NEVER grant the same
/// course twice — the consumer also checks before inserting, but the unique
/// index is the actual correctness guarantee under concurrency.
/// </summary>
public class Enrollment : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid OrderId { get; private set; }

    // Navigation properties
    public Order Order { get; private set; } = null!;
    public Course Course { get; private set; } = null!;

    private Enrollment() { } // EF Core constructor

    public Enrollment(Guid userId, Guid courseId, Guid orderId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (courseId == Guid.Empty)
            throw new ArgumentException("CourseId is required.", nameof(courseId));
        if (orderId == Guid.Empty)
            throw new ArgumentException("OrderId is required.", nameof(orderId));

        UserId = userId;
        CourseId = courseId;
        OrderId = orderId;
    }
}
