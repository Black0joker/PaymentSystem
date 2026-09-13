using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;
using PaymentEntity = Payment.Domain.Entities.Payment;

namespace Payment.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<PaymentEntity>
{
    public void Configure(EntityTypeBuilder<PaymentEntity> builder)
    {
        builder.HasKey(p => p.Id);

        builder.HasIndex(p => p.OrderId);

        builder.HasIndex(p => new { p.Provider, p.ProviderPaymentId })
            .IsUnique()
            .HasFilter("[ProviderPaymentId] IS NOT NULL");

        builder.Property(p => p.Provider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(p => p.ProviderPaymentId)
            .HasMaxLength(256);

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.HasMany(p => p.Attempts)
            .WithOne(a => a.Payment)
            .HasForeignKey(a => a.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
