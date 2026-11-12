namespace Scheduling.Domain.Common;

/// <summary>
/// Tunable booking rules (R5, R6, R9). The Application layer builds it from configuration,
/// so the domain stays free of the Options pattern.
/// </summary>
public sealed record SchedulingPolicy(
    TimeSpan MinimumLeadTime,
    TimeSpan MaximumAdvance,
    TimeSpan SlotGranularity,
    TimeSpan CustomerCancellationWindow)
{
    /// <summary>1h minimum lead, 60 days maximum advance, 15-minute grid, 2h cancellation window.</summary>
    public static SchedulingPolicy Default { get; } = new(
        MinimumLeadTime: TimeSpan.FromHours(1),
        MaximumAdvance: TimeSpan.FromDays(60),
        SlotGranularity: TimeSpan.FromMinutes(15),
        CustomerCancellationWindow: TimeSpan.FromHours(2));

    /// <summary>
    /// R6: start times follow the grid. Checked in UTC: every real time zone offset is a multiple
    /// of 15 minutes, so a 15-minute grid in UTC is also a 15-minute grid in local time.
    /// </summary>
    public bool IsOnGrid(DateTimeOffset instant) =>
        instant.ToUniversalTime().Ticks % SlotGranularity.Ticks == 0;
}
