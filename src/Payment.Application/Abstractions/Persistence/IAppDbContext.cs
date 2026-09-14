using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using CourseEntity = Payment.Domain.Entities.Course;
using OrderEntity = Payment.Domain.Entities.Order;
using PaymentEntity = Payment.Domain.Entities.Payment;
using Payment.Domain.Entities;

namespace Payment.Application.Abstractions.Persistence;

/// <summary>
/// Application-layer abstraction over the database context.
/// The Application layer never references EF Core's concrete DbContext.
/// </summary>
public interface IAppDbContext
{
    DbSet<CourseEntity> Courses { get; }
    DbSet<User> Users { get; }
    DbSet<OrderEntity> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<PaymentEntity> Payments { get; }
    DbSet<PaymentAttempt> PaymentAttempts { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<Enrollment> Enrollments { get; }
    DbSet<AnalyticsEvent> AnalyticsEvents { get; }

    /// <summary>
    /// Exposed so failure paths can discard partial in-memory changes
    /// (see webhook failure handling, Phase 7).
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
