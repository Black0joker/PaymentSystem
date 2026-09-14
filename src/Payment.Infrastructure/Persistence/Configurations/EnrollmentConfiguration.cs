using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Payment.Domain.Entities;

namespace Payment.Infrastructure.Persistence.Configurations;

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.HasKey(e => e.Id);

        // Phase 12 idempotency invariant:
        // duplicate OrderPaidEvent deliveries can never grant the same
        // course twice — the database rejects the second insert.
        builder.HasIndex(e => new { e.UserId, e.CourseId })
            .IsUnique()
            .HasDatabaseName("UX_Enrollments_UserId_CourseId");

        builder.HasIndex(e => e.OrderId);
        builder.HasIndex(e => e.CourseId);

        builder.HasOne(e => e.Order)
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Course)
            .WithMany()
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
