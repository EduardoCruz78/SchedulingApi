using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;

namespace Scheduling.Application.Availability;

/// <summary>
/// Computes the free start times of a professional for a service on a local date.
/// Pure function: no database, no clock. Everything it needs is passed in, so it is exhaustively unit-tested.
/// </summary>
public static class AvailabilityCalculator
{
    /// <param name="professional">Professional with working hours and time off loaded.</param>
    /// <param name="service">Service being booked (defines the slot length).</param>
    /// <param name="localDate">Day in the business time zone.</param>
    /// <param name="appointments">Appointments of this professional around that day. Cancelled ones are ignored.</param>
    /// <param name="now">Current instant, used for the lead-time rules (R5).</param>
    /// <param name="zone">Business time zone.</param>
    /// <param name="policy">Grid and lead-time settings.</param>
    /// <returns>Free slots ordered by start, in UTC.</returns>
    public static IReadOnlyList<TimeSlot> GetFreeSlots(
        Professional professional,
        Service service,
        DateOnly localDate,
        IEnumerable<Appointment> appointments,
        DateTimeOffset now,
        TimeZoneInfo zone,
        SchedulingPolicy policy)
    {
        var busy = appointments
            .Where(a => a.IsActive && a.ProfessionalId == professional.Id)
            .ToList();

        var slots = new List<TimeSlot>();

        // 1. Working intervals of that weekday, in local time.
        foreach (var hours in professional.WorkingHoursOn(localDate.DayOfWeek))
        {
            var intervalEnd = WorkingCalendar.ToUtc(localDate, hours.End, zone);

            // 2. Candidates every grid step, aligned to the grid (09:10 -> 09:15).
            foreach (var candidate in GridTimes(hours.Start, hours.End, policy.SlotGranularity))
            {
                // Local times inside a DST gap do not exist on the clock.
                if (!WorkingCalendar.Exists(localDate, candidate, zone))
                {
                    continue;
                }

                var start = WorkingCalendar.ToUtc(localDate, candidate, zone);
                var end = start + service.Duration;

                if (end > intervalEnd)
                {
                    continue; // service does not fit before the interval closes
                }

                // 3. Collisions with appointments and time off. 4. Lead-time rules.
                if (busy.Exists(a => a.Overlaps(start, end)) ||
                    professional.IsOnTimeOff(start, end) ||
                    start - now < policy.MinimumLeadTime ||
                    start - now > policy.MaximumAdvance)
                {
                    continue;
                }

                slots.Add(new TimeSlot(start, end));
            }
        }

        // 5. UTC, ordered, without duplicates (ambiguous "fall back" times can map to the same instant).
        return slots.Distinct().OrderBy(s => s.StartsAt).ToList();
    }

    private static IEnumerable<TimeOnly> GridTimes(TimeOnly start, TimeOnly end, TimeSpan step)
    {
        var stepTicks = step.Ticks;
        var firstTicks = (start.Ticks + stepTicks - 1) / stepTicks * stepTicks;

        for (var ticks = firstTicks; ticks < end.Ticks; ticks += stepTicks)
        {
            yield return new TimeOnly(ticks);
        }
    }
}
