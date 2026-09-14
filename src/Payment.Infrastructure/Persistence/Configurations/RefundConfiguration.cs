using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence.Configurations;

public class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.HasKey(r => r.Id);

        builder.HasIndex(r => r.PaymentId);
        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.ProviderRefundId);

        builder.Property(r => r.Amount)
            .HasPrecision(18, 2);

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.ProviderRefundId)
            .HasMaxLength(256);

        builder.Property(r => r.Reason)
            .HasMaxLength(500);

        builder.Property(r => r.FailureReason)
            .HasMaxLength(2000);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.HasOne(r => r.Payment)
            .WithMany()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
