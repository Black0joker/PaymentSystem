using Microsoft.EntityFrameworkCore;
using Payment.Domain.Entities;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Application.Abstractions.Persistence;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Course> Courses { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<PaymentEntity> Payments { get; }
    DbSet<PaymentAttempt> PaymentAttempts { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
