using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Payment.Domain.Entities;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Application.Abstractions.Persistence;

public interface IAppDbContext
{
    /// <summary>
    /// Exposed so handlers can discard tracked changes on failure paths
    /// (e.g. revert partial domain mutations before recording a failed webhook).
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    DbSet<User> Users { get; }
    DbSet<Course> Courses { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<PaymentEntity> Payments { get; }
    DbSet<PaymentAttempt> PaymentAttempts { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
