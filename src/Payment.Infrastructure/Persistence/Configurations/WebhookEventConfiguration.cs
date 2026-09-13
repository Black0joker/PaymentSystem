using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence.Configurations;

public class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.HasKey(we => we.Id);

        builder.HasIndex(we => new { we.Provider, we.ProviderEventId })
            .IsUnique();

        builder.HasIndex(we => we.Status);
        builder.HasIndex(we => we.ReceivedAt);

        builder.Property(we => we.Provider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(we => we.ProviderEventId)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(we => we.EventType)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(we => we.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(we => we.Payload)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        builder.Property(we => we.Error)
            .HasMaxLength(2000);
    }
}
