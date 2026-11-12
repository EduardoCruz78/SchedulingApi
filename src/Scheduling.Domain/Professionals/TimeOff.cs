using Scheduling.Domain.Common;

namespace Scheduling.Domain.Professionals;

/// <summary>A period the professional is unavailable (vacation, day off). Part of the Professional aggregate.</summary>
public sealed class TimeOff : Entity
{
    public const int ReasonMaxLength = 200;

    internal TimeOff(Guid id, DateTimeOffset startsAt, DateTimeOffset endsAt, string? reason)
        : base(id)
    {
        StartsAt = startsAt;
        EndsAt = endsAt;
        Reason = reason;
    }

    private TimeOff()
    {
    }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>Half-open interval [StartsAt, EndsAt): touching intervals do not overlap.</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => StartsAt < end && start < EndsAt;
}
