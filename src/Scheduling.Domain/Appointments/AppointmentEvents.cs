using Scheduling.Domain.Common;

namespace Scheduling.Domain.Appointments;

/// <summary>A customer booked a slot. Published to WebhookRelay as <c>appointment.created</c>.</summary>
public sealed record AppointmentBooked(
    Guid AppointmentId,
    Guid CustomerId,
    Guid ProfessionalId,
    Guid ServiceId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal Price,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>An appointment was cancelled and its slot freed. Published as <c>appointment.cancelled</c>.</summary>
public sealed record AppointmentCancelled(
    Guid AppointmentId,
    Guid CustomerId,
    Guid ProfessionalId,
    DateTimeOffset StartsAt,
    bool ByAdmin,
    string? Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;
