using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;

namespace Scheduling.Infrastructure.Persistence.Configurations;

internal sealed class ProfessionalConfiguration : IEntityTypeConfiguration<Professional>
{
    public void Configure(EntityTypeBuilder<Professional> builder)
    {
        builder.ToTable("professionals");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name).HasMaxLength(Professional.NameMaxLength).IsRequired();
        builder.Property(p => p.IsActive);

        // Optimistic concurrency: PostgreSQL's xmin system column changes on every update of the row.
        builder.Property<uint>("Version").IsRowVersion();

        builder.Ignore(p => p.ServiceIds);

        // N:N with services, as rows owned by the aggregate. The private field is the only navigation.
        builder.OwnsMany<ProfessionalService>("_services", services =>
        {
            services.ToTable("professional_services");
            services.WithOwner().HasForeignKey("ProfessionalId");
            services.Property(s => s.ServiceId);
            services.HasKey("ProfessionalId", nameof(ProfessionalService.ServiceId));
            services.HasOne<Service>().WithMany().HasForeignKey(s => s.ServiceId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.OwnsMany(p => p.WorkingHours, hours =>
        {
            hours.ToTable("professional_working_hours", t =>
                t.HasCheckConstraint("ck_professional_working_hours_interval", "start_time < end_time"));
            hours.WithOwner().HasForeignKey("ProfessionalId");
            hours.Property<int>("Id").ValueGeneratedOnAdd();
            hours.HasKey("Id");
            hours.Property(h => h.Day).HasColumnName("day_of_week");
            hours.Property(h => h.Start).HasColumnName("start_time");
            hours.Property(h => h.End).HasColumnName("end_time");
        });
        builder.Navigation(p => p.WorkingHours).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.TimeOffs)
            .WithOne()
            .HasForeignKey("ProfessionalId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.TimeOffs).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TimeOffConfiguration : IEntityTypeConfiguration<TimeOff>
{
    public void Configure(EntityTypeBuilder<TimeOff> builder)
    {
        builder.ToTable("time_offs", t => t.HasCheckConstraint("ck_time_offs_interval", "ends_at > starts_at"));
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Reason).HasMaxLength(TimeOff.ReasonMaxLength);
        builder.HasIndex("ProfessionalId", nameof(TimeOff.StartsAt));
    }
}
