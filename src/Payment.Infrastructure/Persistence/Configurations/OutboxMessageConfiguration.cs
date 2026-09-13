using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.EventType)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(o => o.Payload)
            .IsRequired();

        builder.Property(o => o.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(o => o.RetryCount)
            .IsRequired();

        builder.Property(o => o.Error)
            .HasMaxLength(2000);

        // The background worker polls by (Status, NextAttemptAt) — index it.
        builder.HasIndex(o => new { o.Status, o.NextAttemptAt })
            .HasDatabaseName("IX_OutboxMessages_Status_NextAttemptAt");
    }
}
