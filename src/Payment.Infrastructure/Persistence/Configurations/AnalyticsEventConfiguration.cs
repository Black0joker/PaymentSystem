using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence.Configurations;

public class AnalyticsEventConfiguration : IEntityTypeConfiguration<AnalyticsEvent>
{
    public void Configure(EntityTypeBuilder<AnalyticsEvent> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.Payload)
            .IsRequired();

        builder.HasIndex(a => a.EventType);
        builder.HasIndex(a => a.OccurredAt);
    }
}
