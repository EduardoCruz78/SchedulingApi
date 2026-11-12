namespace Scheduling.Domain.Professionals;

/// <summary>
/// A working interval on a weekday, in the business's local time zone (ADR 0004).
/// A split shift (morning + afternoon) is two intervals on the same day.
/// </summary>
public sealed record WorkingHours(DayOfWeek Day, TimeOnly Start, TimeOnly End)
{
    public bool Overlaps(WorkingHours other) =>
        Day == other.Day && Start < other.End && other.Start < End;
}
