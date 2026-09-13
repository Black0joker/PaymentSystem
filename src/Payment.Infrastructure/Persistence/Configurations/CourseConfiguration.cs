using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence.Configurations;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(c => c.Description)
            .HasMaxLength(2000);

        builder.Property(c => c.Price)
            .HasPrecision(18, 2);

        builder.Property(c => c.Currency)
            .IsRequired()
            .HasMaxLength(3);
    }
}
