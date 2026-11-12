using System.Globalization;
using FluentValidation;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Domain.Common;
using Scheduling.Domain.Professionals;

namespace Scheduling.Application.Professionals;

// ---------- Read models ----------

public sealed record WorkingHoursDto(DayOfWeek Day, string Start, string End);

public sealed record TimeOffDto(Guid Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason)
{
    public static TimeOffDto From(TimeOff t) => new(t.Id, t.StartsAt, t.EndsAt, t.Reason);
}

/// <summary>Professional as returned by the API. <c>TimeOffs</c> is filled only for admins (reasons can be personal).</summary>
public sealed record ProfessionalDto(
    Guid Id,
    string Name,
    bool IsActive,
    IReadOnlyList<Guid> ServiceIds,
    IReadOnlyList<WorkingHoursDto> WorkingHours,
    IReadOnlyList<TimeOffDto>? TimeOffs = null)
{
    public static ProfessionalDto From(Professional p, bool includeTimeOffs = false) => new(
        p.Id,
        p.Name,
        p.IsActive,
        p.ServiceIds.ToList(),
        p.WorkingHours.Select(h => new WorkingHoursDto(h.Day, TimeFormat.Format(h.Start), TimeFormat.Format(h.End))).ToList(),
        includeTimeOffs ? p.TimeOffs.OrderBy(t => t.StartsAt).Select(TimeOffDto.From).ToList() : null);
}

/// <summary>Working hours travel as "HH:mm" strings: friendlier than TimeOnly's "HH:mm:ss" JSON format.</summary>
public static class TimeFormat
{
    private static readonly string[] Formats = ["HH:mm", "HH:mm:ss"];

    public static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static bool TryParse(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

public static class ProfessionalApplicationErrors
{
    public static Error UnknownServices(IEnumerable<Guid> ids) => Error.BusinessRule(
        "Professional.UnknownServices",
        $"These services do not exist: {string.Join(", ", ids)}.");

    /// <summary>R13: time off cannot hide appointments that are already booked.</summary>
    public static readonly Error TimeOffConflictsWithAppointments = Error.Conflict(
        "Professional.TimeOffConflictsWithAppointments",
        "The professional has scheduled appointments in this period. Cancel or move them first.");
}

// ---------- Queries ----------

public sealed class ListProfessionalsHandler(IProfessionalRepository professionals)
{
    public async Task<PagedList<ProfessionalDto>> HandleAsync(
        Guid? serviceId, bool includeInactive, PageRequest page, CancellationToken cancellationToken = default) =>
        (await professionals.ListAsync(serviceId, includeInactive, page, cancellationToken)).Map(p => ProfessionalDto.From(p));
}

public sealed class GetProfessionalHandler(IProfessionalRepository professionals)
{
    /// <summary>Inactive professionals are hidden from the public (404) but visible to admins.</summary>
    public async Task<Result<ProfessionalDto>> HandleAsync(Guid id, bool asAdmin, CancellationToken cancellationToken = default)
    {
        var professional = await professionals.GetByIdAsync(id, cancellationToken);
        if (professional is null || (!professional.IsActive && !asAdmin))
        {
            return ProfessionalErrors.NotFound;
        }

        return ProfessionalDto.From(professional, includeTimeOffs: asAdmin);
    }
}

// ---------- Create / update ----------

public sealed record SaveProfessionalCommand(string Name, bool IsActive = true);

public sealed class SaveProfessionalValidator : AbstractValidator<SaveProfessionalCommand>
{
    public SaveProfessionalValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(Professional.NameMaxLength);
}

public sealed class CreateProfessionalHandler(
    IValidator<SaveProfessionalCommand> validator, IProfessionalRepository professionals, IUnitOfWork unitOfWork)
{
    public async Task<Result<ProfessionalDto>> HandleAsync(SaveProfessionalCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var created = Professional.Create(command.Name);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (!command.IsActive)
        {
            created.Value.Deactivate();
        }

        professionals.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ProfessionalDto.From(created.Value, includeTimeOffs: true);
    }
}

public sealed class UpdateProfessionalHandler(
    IValidator<SaveProfessionalCommand> validator, IProfessionalRepository professionals, IUnitOfWork unitOfWork)
{
    public async Task<Result<ProfessionalDto>> HandleAsync(Guid id, SaveProfessionalCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var professional = await professionals.GetByIdAsync(id, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound;
        }

        var renamed = professional.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed.Error;
        }

        if (command.IsActive)
        {
            professional.Activate();
        }
        else
        {
            professional.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ProfessionalDto.From(professional, includeTimeOffs: true);
    }
}

// ---------- Services performed ----------

public sealed record SetProfessionalServicesCommand(IReadOnlyList<Guid> ServiceIds);

public sealed class SetProfessionalServicesValidator : AbstractValidator<SetProfessionalServicesCommand>
{
    public SetProfessionalServicesValidator()
    {
        RuleFor(c => c.ServiceIds).NotNull();
        RuleForEach(c => c.ServiceIds).NotEmpty();
    }
}

public sealed class SetProfessionalServicesHandler(
    IValidator<SetProfessionalServicesCommand> validator,
    IProfessionalRepository professionals,
    IServiceRepository services,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<ProfessionalDto>> HandleAsync(Guid id, SetProfessionalServicesCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var professional = await professionals.GetByIdAsync(id, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound;
        }

        var ids = command.ServiceIds.Distinct().ToList();
        var missing = await services.FindMissingAsync(ids, cancellationToken);
        if (missing.Count > 0)
        {
            return ProfessionalApplicationErrors.UnknownServices(missing);
        }

        professional.SetServices(ids);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ProfessionalDto.From(professional, includeTimeOffs: true);
    }
}

// ---------- Working hours ----------

public sealed record WorkingHoursInput(DayOfWeek Day, string Start, string End);

public sealed record SetWorkingHoursCommand(IReadOnlyList<WorkingHoursInput> Intervals);

public sealed class SetWorkingHoursValidator : AbstractValidator<SetWorkingHoursCommand>
{
    public SetWorkingHoursValidator()
    {
        RuleFor(c => c.Intervals).NotNull();
        RuleForEach(c => c.Intervals).ChildRules(interval =>
        {
            interval.RuleFor(i => i.Day).IsInEnum();
            interval.RuleFor(i => i.Start).Must(v => TimeFormat.TryParse(v, out _)).WithMessage("Use the HH:mm format.");
            interval.RuleFor(i => i.End).Must(v => TimeFormat.TryParse(v, out _)).WithMessage("Use the HH:mm format.");
        });
    }
}

/// <summary>Replaces the weekly schedule. R11 (start before end, no overlaps per day) is enforced by the domain.</summary>
public sealed class SetWorkingHoursHandler(
    IValidator<SetWorkingHoursCommand> validator, IProfessionalRepository professionals, IUnitOfWork unitOfWork)
{
    public async Task<Result<ProfessionalDto>> HandleAsync(Guid id, SetWorkingHoursCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var professional = await professionals.GetByIdAsync(id, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound;
        }

        // The validator already guaranteed both values parse.
        var intervals = command.Intervals.Select(i =>
            TimeFormat.TryParse(i.Start, out var start) && TimeFormat.TryParse(i.End, out var end)
                ? new WorkingHours(i.Day, start, end)
                : throw new InvalidOperationException("Working hours were not validated."));

        var result = professional.SetWorkingHours(intervals);
        if (result.IsFailure)
        {
            return result.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ProfessionalDto.From(professional, includeTimeOffs: true);
    }
}

// ---------- Time off ----------

public sealed record AddTimeOffCommand(DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Reason);

public sealed class AddTimeOffValidator : AbstractValidator<AddTimeOffCommand>
{
    public AddTimeOffValidator()
    {
        RuleFor(c => c.StartsAt).NotEmpty();
        RuleFor(c => c.EndsAt).NotEmpty().GreaterThan(c => c.StartsAt).WithMessage("End must be after start.");
        RuleFor(c => c.Reason).MaximumLength(TimeOff.ReasonMaxLength);
    }
}

public sealed class AddTimeOffHandler(
    IValidator<AddTimeOffCommand> validator,
    IProfessionalRepository professionals,
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<TimeOffDto>> HandleAsync(Guid professionalId, AddTimeOffCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var professional = await professionals.GetByIdAsync(professionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound;
        }

        // R13
        if (await appointments.ProfessionalHasOverlapAsync(professionalId, command.StartsAt, command.EndsAt, cancellationToken))
        {
            return ProfessionalApplicationErrors.TimeOffConflictsWithAppointments;
        }

        var added = professional.AddTimeOff(command.StartsAt, command.EndsAt, command.Reason, timeProvider.GetUtcNow());
        if (added.IsFailure)
        {
            return added.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return TimeOffDto.From(added.Value);
    }
}

public sealed class RemoveTimeOffHandler(IProfessionalRepository professionals, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid professionalId, Guid timeOffId, CancellationToken cancellationToken = default)
    {
        var professional = await professionals.GetByIdAsync(professionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound;
        }

        var removed = professional.RemoveTimeOff(timeOffId);
        if (removed.IsFailure)
        {
            return removed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
