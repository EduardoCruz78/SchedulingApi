using FluentValidation;
using Microsoft.Extensions.Options;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Availability;
using Scheduling.Application.Common;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;

namespace Scheduling.Application.Appointments;

public sealed record AppointmentDto(
    Guid Id,
    Guid CustomerId,
    Guid ProfessionalId,
    Guid ServiceId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    DateTimeOffset LocalStartsAt,
    decimal PriceAtBooking,
    AppointmentStatus Status,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    DateTimeOffset CreatedAt)
{
    public static AppointmentDto From(Appointment a, TimeZoneInfo zone) => new(
        a.Id,
        a.CustomerId,
        a.ProfessionalId,
        a.ServiceId,
        a.StartsAt,
        a.EndsAt,
        TimeZoneInfo.ConvertTime(a.StartsAt, zone),
        a.PriceAtBooking,
        a.Status,
        a.CancelledAt,
        a.CancellationReason,
        a.CreatedAt);
}

/// <summary>Shared lookups for the appointment use cases.</summary>
public sealed class AppointmentContext(
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    IOptions<BusinessOptions> businessOptions,
    IOptions<SchedulingOptions> schedulingOptions,
    TimeProvider timeProvider)
{
    public IAppointmentRepository Appointments => appointments;

    public IUnitOfWork UnitOfWork => unitOfWork;

    public TimeZoneInfo Zone { get; } = businessOptions.Value.GetTimeZone();

    public SchedulingPolicy Policy { get; } = schedulingOptions.Value.ToPolicy();

    public DateTimeOffset Now => timeProvider.GetUtcNow();

    public AppointmentDto ToDto(Appointment appointment) => AppointmentDto.From(appointment, Zone);

    /// <summary>
    /// Owner or admin only. Someone else's appointment answers 404, not 403,
    /// so the API does not reveal that the id exists.
    /// </summary>
    public async Task<Result<Appointment>> GetVisibleAsync(Guid id, Guid userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var appointment = await appointments.GetByIdAsync(id, cancellationToken);
        return appointment is not null && (isAdmin || appointment.CustomerId == userId)
            ? appointment
            : AppointmentErrors.NotFound;
    }
}

// ---------- Book ----------

public sealed record BookAppointmentCommand(Guid ProfessionalId, Guid ServiceId, DateTimeOffset StartsAt);

public sealed class BookAppointmentValidator : AbstractValidator<BookAppointmentCommand>
{
    public BookAppointmentValidator()
    {
        RuleFor(c => c.ProfessionalId).NotEmpty();
        RuleFor(c => c.ServiceId).NotEmpty();
        RuleFor(c => c.StartsAt).NotEmpty();
    }
}

public sealed class BookAppointmentHandler(
    IValidator<BookAppointmentCommand> validator,
    IProfessionalRepository professionals,
    IServiceRepository services,
    AppointmentContext context)
{
    public async Task<Result<AppointmentDto>> HandleAsync(Guid customerId, BookAppointmentCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var professional = await professionals.GetByIdAsync(command.ProfessionalId, cancellationToken);
        if (professional is null)
        {
            return ProfessionalErrors.NotFound;
        }

        var service = await services.GetByIdAsync(command.ServiceId, cancellationToken);
        if (service is null)
        {
            return ServiceErrors.NotFound;
        }

        // R7
        if (!professional.IsActive)
        {
            return ProfessionalErrors.Inactive;
        }

        if (!service.IsActive)
        {
            return ServiceErrors.Inactive;
        }

        if (!professional.Performs(service.Id))
        {
            return ProfessionalErrors.DoesNotPerformService;
        }

        // R5, R6, R8
        var booked = Appointment.Book(customerId, professional.Id, service, command.StartsAt, context.Now, context.Policy);
        if (booked.IsFailure)
        {
            return booked.Error;
        }

        var appointment = booked.Value;

        // R1
        if (!WorkingCalendar.FitsWorkingHours(professional, appointment.StartsAt, appointment.EndsAt, context.Zone))
        {
            return AppointmentErrors.OutsideWorkingHours;
        }

        // R4
        if (professional.IsOnTimeOff(appointment.StartsAt, appointment.EndsAt))
        {
            return AppointmentErrors.ProfessionalOnTimeOff;
        }

        // R2 (friendly check; the exclusion constraint is the guarantee)
        if (await context.Appointments.ProfessionalHasOverlapAsync(professional.Id, appointment.StartsAt, appointment.EndsAt, cancellationToken))
        {
            return AppointmentErrors.SlotTaken;
        }

        // R3
        if (await context.Appointments.CustomerHasOverlapAsync(customerId, appointment.StartsAt, appointment.EndsAt, cancellationToken))
        {
            return AppointmentErrors.CustomerOverlap;
        }

        context.Appointments.Add(appointment);
        try
        {
            await context.UnitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException ex) when (ex.Kind == ConflictKind.Overlap)
        {
            // ADR 0003: another request took the slot between our check and our insert.
            return AppointmentErrors.SlotTaken;
        }

        return context.ToDto(appointment);
    }
}

// ---------- Queries ----------

public sealed record AppointmentQuery(AppointmentStatus? Status, DateTimeOffset? From, DateTimeOffset? To, Guid? ProfessionalId = null);

public sealed class AppointmentQueryValidator : AbstractValidator<AppointmentQuery>
{
    public AppointmentQueryValidator() =>
        RuleFor(q => q.To)
            .GreaterThan(q => q.From)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage("'to' must be after 'from'.");
}

public sealed class GetAppointmentHandler(AppointmentContext context)
{
    public async Task<Result<AppointmentDto>> HandleAsync(Guid id, Guid userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var appointment = await context.GetVisibleAsync(id, userId, isAdmin, cancellationToken);
        return appointment.IsSuccess ? context.ToDto(appointment.Value) : appointment.Error;
    }
}

public sealed class ListMyAppointmentsHandler(IValidator<AppointmentQuery> validator, AppointmentContext context)
{
    public async Task<Result<PagedList<AppointmentDto>>> HandleAsync(
        Guid customerId, AppointmentQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var filter = new AppointmentFilter(CustomerId: customerId, Status: query.Status, From: query.From, To: query.To);
        return (await context.Appointments.SearchAsync(filter, page, cancellationToken)).Map(context.ToDto);
    }
}

public sealed class ListAppointmentsHandler(IValidator<AppointmentQuery> validator, AppointmentContext context)
{
    public async Task<Result<PagedList<AppointmentDto>>> HandleAsync(
        AppointmentQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(query, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var filter = new AppointmentFilter(ProfessionalId: query.ProfessionalId, Status: query.Status, From: query.From, To: query.To);
        return (await context.Appointments.SearchAsync(filter, page, cancellationToken)).Map(context.ToDto);
    }
}

// ---------- State changes ----------

public sealed record CancelAppointmentCommand(string? Reason);

public sealed class CancelAppointmentHandler(AppointmentContext context)
{
    /// <summary>R9: owner up to the cancellation window, admin at any time. Frees the slot.</summary>
    public async Task<Result<AppointmentDto>> HandleAsync(
        Guid id, Guid userId, bool isAdmin, CancelAppointmentCommand command, CancellationToken cancellationToken = default)
    {
        var found = await context.GetVisibleAsync(id, userId, isAdmin, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var cancelled = found.Value.Cancel(isAdmin, command.Reason, context.Now, context.Policy);
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        await context.UnitOfWork.SaveChangesAsync(cancellationToken);
        return context.ToDto(found.Value);
    }
}

public sealed class CompleteAppointmentHandler(AppointmentContext context)
{
    /// <summary>R10: admin only, after the start.</summary>
    public async Task<Result<AppointmentDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Transition(context, id, (a, now) => a.Complete(now), cancellationToken);

    internal static async Task<Result<AppointmentDto>> Transition(
        AppointmentContext context, Guid id, Func<Appointment, DateTimeOffset, Result> change, CancellationToken cancellationToken)
    {
        var appointment = await context.Appointments.GetByIdAsync(id, cancellationToken);
        if (appointment is null)
        {
            return AppointmentErrors.NotFound;
        }

        var changed = change(appointment, context.Now);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await context.UnitOfWork.SaveChangesAsync(cancellationToken);
        return context.ToDto(appointment);
    }
}

public sealed class MarkNoShowHandler(AppointmentContext context)
{
    /// <summary>R10: admin only, after the start.</summary>
    public async Task<Result<AppointmentDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default) =>
        await CompleteAppointmentHandler.Transition(context, id, (a, now) => a.MarkNoShow(now), cancellationToken);
}
