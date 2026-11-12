using Scheduling.Domain.Common;

namespace Scheduling.Domain.Professionals;

public sealed class Professional : AggregateRoot
{
    public const int NameMaxLength = 100;

    private readonly List<ProfessionalService> _services = [];
    private readonly List<WorkingHours> _workingHours = [];
    private readonly List<TimeOff> _timeOffs = [];

    private Professional(Guid id, string name)
        : base(id)
    {
        Name = name;
        IsActive = true;
    }

    private Professional()
    {
    }

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; }

    /// <summary>Services this professional performs, referenced by id (another aggregate).</summary>
    public IReadOnlyCollection<Guid> ServiceIds => _services.Select(s => s.ServiceId).ToList();

    public IReadOnlyCollection<WorkingHours> WorkingHours => _workingHours.AsReadOnly();

    public IReadOnlyCollection<TimeOff> TimeOffs => _timeOffs.AsReadOnly();

    public static Result<Professional> Create(string name)
    {
        name = name?.Trim() ?? string.Empty;
        return IsValidName(name) ? new Professional(Guid.CreateVersion7(), name) : ProfessionalErrors.InvalidName;
    }

    public Result Rename(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (!IsValidName(name))
        {
            return ProfessionalErrors.InvalidName;
        }

        Name = name;
        return Result.Success();
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public bool Performs(Guid serviceId) => _services.Exists(s => s.ServiceId == serviceId);

    /// <summary>Replaces the whole list of services (PUT semantics). Duplicates are ignored.</summary>
    public void SetServices(IEnumerable<Guid> serviceIds)
    {
        _services.Clear();
        _services.AddRange(serviceIds.Distinct().Select(id => new ProfessionalService(id)));
    }

    /// <summary>R11: replaces the weekly schedule. Each interval needs Start &lt; End and no overlaps on the same day.</summary>
    public Result SetWorkingHours(IEnumerable<WorkingHours> workingHours)
    {
        var intervals = workingHours.ToList();

        if (intervals.Exists(i => i.Start >= i.End))
        {
            return ProfessionalErrors.InvalidWorkingHoursInterval;
        }

        var overlapping = intervals
            .GroupBy(i => i.Day)
            .Any(day => day.OrderBy(i => i.Start).Zip(day.OrderBy(i => i.Start).Skip(1)).Any(pair => pair.First.Overlaps(pair.Second)));

        if (overlapping)
        {
            return ProfessionalErrors.OverlappingWorkingHours;
        }

        _workingHours.Clear();
        _workingHours.AddRange(intervals.OrderBy(i => i.Day).ThenBy(i => i.Start));
        return Result.Success();
    }

    public IEnumerable<WorkingHours> WorkingHoursOn(DayOfWeek day) =>
        _workingHours.Where(i => i.Day == day).OrderBy(i => i.Start);

    public Result<TimeOff> AddTimeOff(DateTimeOffset startsAt, DateTimeOffset endsAt, string? reason, DateTimeOffset now)
    {
        startsAt = startsAt.ToUtc();
        endsAt = endsAt.ToUtc();
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (startsAt >= endsAt)
        {
            return ProfessionalErrors.InvalidTimeOffInterval;
        }

        if (endsAt <= now)
        {
            return ProfessionalErrors.TimeOffInThePast;
        }

        if (reason?.Length > TimeOff.ReasonMaxLength)
        {
            return ProfessionalErrors.InvalidTimeOffReason;
        }

        if (_timeOffs.Exists(t => t.Overlaps(startsAt, endsAt)))
        {
            return ProfessionalErrors.OverlappingTimeOff;
        }

        var timeOff = new TimeOff(Guid.CreateVersion7(), startsAt, endsAt, reason);
        _timeOffs.Add(timeOff);
        return timeOff;
    }

    public Result RemoveTimeOff(Guid timeOffId)
    {
        var removed = _timeOffs.RemoveAll(t => t.Id == timeOffId);
        return removed > 0 ? Result.Success() : ProfessionalErrors.TimeOffNotFound;
    }

    public bool IsOnTimeOff(DateTimeOffset start, DateTimeOffset end) => _timeOffs.Exists(t => t.Overlaps(start, end));

    private static bool IsValidName(string name) => name.Length is > 0 and <= NameMaxLength;
}

/// <summary>Join row between a professional and a service id. Internal to the aggregate.</summary>
public sealed class ProfessionalService
{
    internal ProfessionalService(Guid serviceId) => ServiceId = serviceId;

    public Guid ServiceId { get; private set; }
}

public static class ProfessionalErrors
{
    public static readonly Error InvalidName =
        Error.Validation("Professional.InvalidName", $"Name is required and must have at most {Professional.NameMaxLength} characters.");

    public static readonly Error NotFound =
        Error.NotFound("Professional.NotFound", "Professional not found.");

    public static readonly Error Inactive =
        Error.BusinessRule("Professional.Inactive", "This professional is not taking appointments.");

    public static readonly Error DoesNotPerformService =
        Error.BusinessRule("Professional.DoesNotPerformService", "This professional does not perform the selected service.");

    public static readonly Error InvalidWorkingHoursInterval =
        Error.Validation("Professional.InvalidWorkingHoursInterval", "Each working interval must start before it ends.");

    public static readonly Error OverlappingWorkingHours =
        Error.Validation("Professional.OverlappingWorkingHours", "Working intervals on the same day cannot overlap.");

    public static readonly Error InvalidTimeOffInterval =
        Error.Validation("Professional.InvalidTimeOffInterval", "Time off must start before it ends.");

    public static readonly Error TimeOffInThePast =
        Error.BusinessRule("Professional.TimeOffInThePast", "Time off must end in the future.");

    public static readonly Error InvalidTimeOffReason =
        Error.Validation("Professional.InvalidTimeOffReason", $"Reason must have at most {TimeOff.ReasonMaxLength} characters.");

    public static readonly Error OverlappingTimeOff =
        Error.Conflict("Professional.OverlappingTimeOff", "This period overlaps an existing time off.");

    public static readonly Error TimeOffNotFound =
        Error.NotFound("Professional.TimeOffNotFound", "Time off not found.");
}
