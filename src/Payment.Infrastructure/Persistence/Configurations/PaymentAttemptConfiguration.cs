using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence.Configurations;

public class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.HasKey(pa => pa.Id);

        builder.HasIndex(pa => pa.PaymentId);

        builder.Property(pa => pa.Provider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pa => pa.ProviderPaymentId)
            .HasMaxLength(256);

        builder.Property(pa => pa.Amount)
            .HasPrecision(18, 2);

        builder.Property(pa => pa.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(pa => pa.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(pa => pa.FailureReason)
            .HasMaxLength(1000);
    }
}
