using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Availability;
using Scheduling.Application.Common;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Persistence.Seed;

/// <summary>
/// Demo data for Development and the public demo. Idempotent: does nothing if any user exists.
/// Credentials are fictitious and documented in the README.
/// </summary>
public sealed class DataSeeder(
    SchedulingDbContext db,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider,
    IOptions<BusinessOptions> businessOptions,
    IOptions<SchedulingOptions> schedulingOptions,
    ILogger<DataSeeder> logger)
{
    public const string AdminEmail = "admin@scheduling.demo";
    public const string AdminPassword = "Admin123!";
    public const string CustomerPassword = "Customer123!";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var zone = businessOptions.Value.GetTimeZone();
        var policy = schedulingOptions.Value.ToPolicy();

        var admin = User.Create("Demo Admin", AdminEmail, passwordHasher.Hash(AdminPassword), Role.Admin, now).Value;
        var maria = User.Create("Maria Silva", "maria@scheduling.demo", passwordHasher.Hash(CustomerPassword), Role.Customer, now).Value;
        var joao = User.Create("João Souza", "joao@scheduling.demo", passwordHasher.Hash(CustomerPassword), Role.Customer, now).Value;

        var haircut = Service.Create("Haircut", "Classic cut with scissors and clippers.", 30, 50m).Value;
        var beard = Service.Create("Beard trim", "Beard shaping with hot towel.", 15, 30m).Value;
        var combo = Service.Create("Haircut + beard", "Full haircut and beard trim.", 45, 75m).Value;
        var coloring = Service.Create("Hair coloring", "Full coloring with premium products.", 90, 150m).Value;
        var kids = Service.Create("Kids haircut", "For children up to 12 years old.", 30, 40m).Value;

        var weekdays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

        var ana = Professional.Create("Ana Costa").Value;
        ana.SetServices([haircut.Id, kids.Id, coloring.Id]);
        ana.SetWorkingHours(weekdays.SelectMany(d => new[] { Hours(d, 9, 12), Hours(d, 13, 18) }));

        var bruno = Professional.Create("Bruno Lima").Value;
        bruno.SetServices([haircut.Id, beard.Id, combo.Id]);
        bruno.SetWorkingHours(new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }
            .Select(d => Hours(d, 10, 19)));

        var carla = Professional.Create("Carla Mendes").Value;
        carla.SetServices([coloring.Id, haircut.Id]);
        carla.SetWorkingHours(weekdays.Select(d => Hours(d, 8, 14)));

        db.Users.AddRange(admin, maria, joao);
        db.Services.AddRange(haircut, beard, combo, coloring, kids);
        db.Professionals.AddRange(ana, bruno, carla);

        // A few upcoming appointments, placed on real free slots so they always satisfy every rule.
        var bookings = new (User Customer, Professional Professional, Service Service, int DaysAhead)[]
        {
            (maria, ana, haircut, 1),
            (joao, bruno, combo, 2),
            (maria, carla, coloring, 5),
            (joao, ana, kids, 7),
        };

        var booked = new List<Appointment>();
        foreach (var (customer, professional, service, daysAhead) in bookings)
        {
            var slot = FirstFreeSlot(professional, service, now, daysAhead, zone, policy, booked);
            if (slot is null)
            {
                continue;
            }

            var appointment = Appointment.Book(customer.Id, professional.Id, service, slot.StartsAt, now, policy).Value;
            booked.Add(appointment);
        }

        db.Appointments.AddRange(booked);
        await db.SaveChangesAsync(cancellationToken);

        var appointmentCount = booked.Count;
        logger.LogInformation("Seeded demo data: 3 users, 5 services, 3 professionals, {Count} appointments.", appointmentCount);
    }

    private static WorkingHours Hours(DayOfWeek day, int startHour, int endHour) =>
        new(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    private static TimeSlot? FirstFreeSlot(
        Professional professional,
        Service service,
        DateTimeOffset now,
        int daysAhead,
        TimeZoneInfo zone,
        Domain.Common.SchedulingPolicy policy,
        IReadOnlyList<Appointment> booked)
    {
        var today = WorkingCalendar.LocalDate(now, zone);
        for (var offset = daysAhead; offset < daysAhead + 14; offset++)
        {
            var date = today.AddDays(offset);
            var slots = AvailabilityCalculator.GetFreeSlots(professional, service, date, booked, now, zone, policy);

            // Prefer a mid-morning slot so the demo looks natural.
            var preferred = slots.FirstOrDefault(s => TimeZoneInfo.ConvertTime(s.StartsAt, zone).Hour >= 10) ?? (slots.Count > 0 ? slots[0] : null);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return null;
    }
}
