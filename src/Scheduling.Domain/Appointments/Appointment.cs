using Scheduling.Domain.Common;
using Scheduling.Domain.Services;

namespace Scheduling.Domain.Appointments;

public enum AppointmentStatus
{
    Scheduled,
    Cancelled,
    Completed,
    NoShow,
}

/// <summary>
/// A booked slot. Rules that need only the appointment itself (R5, R6, R8, R9, R10) live here.
/// Rules that need other data (working hours, overlaps, time off) are checked by the use case,
/// and overlap for the same professional is also enforced by the database (ADR 0003).
/// </summary>
public sealed class Appointment : AggregateRoot
{
    public const int CancellationReasonMaxLength = 500;

    private Appointment(
        Guid id,
        Guid customerId,
        Guid professionalId,
        Guid serviceId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        decimal priceAtBooking,
        DateTimeOffset createdAt)
        : base(id)
    {
        CustomerId = customerId;
        ProfessionalId = professionalId;
        ServiceId = serviceId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        PriceAtBooking = priceAtBooking;
        Status = AppointmentStatus.Scheduled;
        CreatedAt = createdAt;
    }

    private Appointment()
    {
    }

    public Guid CustomerId { get; private set; }

    public Guid ProfessionalId { get; private set; }

    public Guid ServiceId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>R8: copied at booking time, so later price changes do not rewrite history.</summary>
    public decimal PriceAtBooking { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Blocks the slot: everything except cancelled appointments.</summary>
    public bool IsActive => Status != AppointmentStatus.Cancelled;

    public static Result<Appointment> Book(
        Guid customerId,
        Guid professionalId,
        Service service,
        DateTimeOffset startsAt,
        DateTimeOffset now,
        SchedulingPolicy policy)
    {
        startsAt = startsAt.ToUtc();
        now = now.ToUtc();

        // R6
        if (!policy.IsOnGrid(startsAt))
        {
            return AppointmentErrors.NotOnGrid(policy.SlotGranularity);
        }

        // R5
        if (startsAt - now < policy.MinimumLeadTime)
        {
            return AppointmentErrors.TooSoon(policy.MinimumLeadTime);
        }

        if (startsAt - now > policy.MaximumAdvance)
        {
            return AppointmentErrors.TooFarAhead(policy.MaximumAdvance);
        }

        // R8
        var endsAt = startsAt + service.Duration;

        var appointment = new Appointment(
            Guid.CreateVersion7(),
            customerId,
            professionalId,
            service.Id,
            startsAt,
            endsAt,
            service.Price,
            now);

        appointment.Raise(new AppointmentBooked(
            appointment.Id, customerId, professionalId, service.Id, startsAt, endsAt, service.Price, now));
        return appointment;
    }

    /// <summary>R9: the customer can cancel up to the window before the start; an admin can cancel any time.</summary>
    public Result Cancel(bool byAdmin, string? reason, DateTimeOffset now, SchedulingPolicy policy)
    {
        now = now.ToUtc();
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (Status != AppointmentStatus.Scheduled)
        {
            return AppointmentErrors.InvalidTransition(Status, AppointmentStatus.Cancelled);
        }

        if (reason?.Length > CancellationReasonMaxLength)
        {
            return AppointmentErrors.InvalidCancellationReason;
        }

        if (!byAdmin && StartsAt - now < policy.CustomerCancellationWindow)
        {
            return AppointmentErrors.CancellationWindowClosed(policy.CustomerCancellationWindow);
        }

        Status = AppointmentStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = reason;
        Raise(new AppointmentCancelled(Id, CustomerId, ProfessionalId, StartsAt, byAdmin, reason, now));
        return Result.Success();
    }

    /// <summary>R10: only after the appointment has started.</summary>
    public Result Complete(DateTimeOffset now) => Finish(AppointmentStatus.Completed, now);

    /// <summary>R10: only after the appointment has started.</summary>
    public Result MarkNoShow(DateTimeOffset now) => Finish(AppointmentStatus.NoShow, now);

    /// <summary>Half-open interval [StartsAt, EndsAt): back-to-back appointments do not overlap.</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => StartsAt < end && start < EndsAt;

    private Result Finish(AppointmentStatus target, DateTimeOffset now)
    {
        if (Status != AppointmentStatus.Scheduled)
        {
            return AppointmentErrors.InvalidTransition(Status, target);
        }

        if (now.ToUtc() < StartsAt)
        {
            return AppointmentErrors.NotStartedYet;
        }

        Status = target;
        return Result.Success();
    }
}

public static class AppointmentErrors
{
    public static readonly Error NotFound =
        Error.NotFound("Appointment.NotFound", "Appointment not found.");

    public static readonly Error NotStartedYet =
        Error.BusinessRule("Appointment.NotStartedYet", "The appointment has not started yet.");

    public static readonly Error InvalidCancellationReason = Error.Validation(
        "Appointment.InvalidCancellationReason",
        $"Reason must have at most {Appointment.CancellationReasonMaxLength} characters.");

    public static readonly Error OutsideWorkingHours =
        Error.BusinessRule("Appointment.OutsideWorkingHours", "The selected time is outside the professional's working hours.");

    public static readonly Error ProfessionalOnTimeOff =
        Error.BusinessRule("Appointment.ProfessionalOnTimeOff", "The professional is not available at the selected time.");

    public static readonly Error SlotTaken =
        Error.Conflict("Appointment.SlotTaken", "The selected time is no longer available.");

    public static readonly Error CustomerOverlap =
        Error.Conflict("Appointment.CustomerOverlap", "You already have an appointment at this time.");

    public static Error NotOnGrid(TimeSpan granularity) => Error.BusinessRule(
        "Appointment.NotOnGrid",
        $"Start time must be on a {granularity.TotalMinutes:0}-minute boundary.");

    public static Error TooSoon(TimeSpan minimumLead) => Error.BusinessRule(
        "Appointment.TooSoon",
        $"Appointments must be booked at least {minimumLead.TotalMinutes:0} minutes in advance.");

    public static Error TooFarAhead(TimeSpan maximumAdvance) => Error.BusinessRule(
        "Appointment.TooFarAhead",
        $"Appointments can be booked at most {maximumAdvance.TotalDays:0} days in advance.");

    public static Error CancellationWindowClosed(TimeSpan window) => Error.BusinessRule(
        "Appointment.CancellationWindowClosed",
        $"Appointments can only be cancelled up to {window.TotalMinutes:0} minutes before they start.");

    public static Error InvalidTransition(AppointmentStatus from, AppointmentStatus to) => Error.BusinessRule(
        "Appointment.InvalidTransition",
        $"Cannot change an appointment from {from} to {to}.");
}
