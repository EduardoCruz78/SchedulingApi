using System.ComponentModel.DataAnnotations;
using Scheduling.Domain.Common;

namespace Scheduling.Application.Common;

/// <summary>Booking rules, bound from the "Scheduling" configuration section.</summary>
public sealed class SchedulingOptions
{
    public const string SectionName = "Scheduling";

    [Range(0, 7 * 24 * 60)]
    public int MinimumLeadTimeMinutes { get; init; } = 60;

    [Range(1, 365)]
    public int MaximumAdvanceDays { get; init; } = 60;

    /// <summary>Must divide 60 so the grid lines up with whole hours in every time zone.</summary>
    [AllowedValues(5, 10, 15, 20, 30, 60)]
    public int SlotGranularityMinutes { get; init; } = 15;

    [Range(0, 7 * 24 * 60)]
    public int CustomerCancellationWindowMinutes { get; init; } = 120;

    public SchedulingPolicy ToPolicy() => new(
        TimeSpan.FromMinutes(MinimumLeadTimeMinutes),
        TimeSpan.FromDays(MaximumAdvanceDays),
        TimeSpan.FromMinutes(SlotGranularityMinutes),
        TimeSpan.FromMinutes(CustomerCancellationWindowMinutes));
}

/// <summary>The establishment served by this deployment, bound from the "Business" section.</summary>
public sealed class BusinessOptions
{
    public const string SectionName = "Business";

    /// <summary>IANA time zone id. Working hours are interpreted in this zone (ADR 0004).</summary>
    [Required]
    public string TimeZone { get; init; } = "America/Sao_Paulo";

    public TimeZoneInfo GetTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
}
