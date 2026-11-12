using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Domain.Services;

namespace Scheduling.Infrastructure.Persistence.Configurations;

internal sealed class ServiceConfiguration : IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services", t =>
        {
            // Defense in depth: the domain already validates these.
            t.HasCheckConstraint("ck_services_duration", "duration_minutes BETWEEN 15 AND 480 AND duration_minutes % 15 = 0");
            t.HasCheckConstraint("ck_services_price", "price >= 0");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name).HasMaxLength(Service.NameMaxLength).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(Service.DescriptionMaxLength);
        builder.Property(s => s.DurationMinutes);
        builder.Property(s => s.Price).HasPrecision(10, 2);
        builder.Property(s => s.IsActive);

        builder.Ignore(s => s.Duration);
    }
}
