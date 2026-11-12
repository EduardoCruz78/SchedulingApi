using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence.Configurations;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    /// <summary>Name of the exclusion constraint created by migration (ADR 0003).</summary>
    public const string OverlapConstraintName = "ex_appointments_professional_overlap";

    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", t =>
            t.HasCheckConstraint("ck_appointments_interval", "ends_at > starts_at"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.PriceAtBooking).HasPrecision(10, 2);
        builder.Property(a => a.CancellationReason).HasMaxLength(Appointment.CancellationReasonMaxLength);

        // Optimistic concurrency (e.g. admin completes while the customer cancels): xmin system column.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<User>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Professional>().WithMany().HasForeignKey(a => a.ProfessionalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Service>().WithMany().HasForeignKey(a => a.ServiceId).OnDelete(DeleteBehavior.Restrict);

        // Availability and "my appointments" queries filter by owner and time.
        builder.HasIndex(a => new { a.ProfessionalId, a.StartsAt });
        builder.HasIndex(a => new { a.CustomerId, a.StartsAt });
    }
}
