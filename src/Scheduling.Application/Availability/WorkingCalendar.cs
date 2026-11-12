using Scheduling.Domain.Professionals;

namespace Scheduling.Application.Availability;

/// <summary>A concrete time range in UTC, half-open [StartsAt, EndsAt).</summary>
public sealed record TimeSlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

/// <summary>
/// Converts a professional's weekly schedule (local <see cref="TimeOnly"/> values) into concrete UTC
/// intervals for a given date in the business time zone (ADR 0004). Conversion is per date, so the
/// same "09:00" maps to different UTC instants before and after a daylight saving change.
/// </summary>
public static class WorkingCalendar
{
    /// <summary>Working intervals of the professional on <paramref name="localDate"/>, in UTC.</summary>
    public static IReadOnlyList<TimeSlot> WorkingIntervalsUtc(Professional professional, DateOnly localDate, TimeZoneInfo zone) =>
        professional.WorkingHoursOn(localDate.DayOfWeek)
            .Select(h => new TimeSlot(ToUtc(localDate, h.Start, zone), ToUtc(localDate, h.End, zone)))
            .Where(slot => slot.StartsAt < slot.EndsAt)
            .ToList();

    /// <summary>R1: true when [start, end) fits entirely inside one working interval of that local day.</summary>
    public static bool FitsWorkingHours(Professional professional, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var localDate = LocalDate(start, zone);
        return WorkingIntervalsUtc(professional, localDate, zone)
            .Any(interval => interval.StartsAt <= start && end <= interval.EndsAt);
    }

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>
    /// Converts a local wall-clock time to UTC.
    /// Times skipped by a "spring forward" change do not exist; they are moved to the first valid minute after the gap.
    /// Ambiguous times ("fall back") resolve to the standard-time offset, the .NET default.
    /// </summary>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>True when the local wall-clock time exists on that date (it is not inside a DST gap).</summary>
    public static bool Exists(DateOnly date, TimeOnly time, TimeZoneInfo zone) =>
        !zone.IsInvalidTime(date.ToDateTime(time, DateTimeKind.Unspecified));
}
