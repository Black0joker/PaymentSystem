using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(oi => oi.Id);

        builder.HasIndex(oi => oi.OrderId);
        builder.HasIndex(oi => oi.CourseId);

        builder.Property(oi => oi.UnitPrice)
            .HasPrecision(18, 2);

        builder.Property(oi => oi.TotalPrice)
            .HasPrecision(18, 2);

        builder.HasOne(oi => oi.Course)
            .WithMany(c => c.OrderItems)
            .HasForeignKey(oi => oi.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
