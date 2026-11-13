using System.Text.Json;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;

namespace Scheduling.Infrastructure.Outbox;

/// <summary>
/// Turns domain events into outbox messages. The type names and payload fields are the public
/// contract with WebhookRelay and its subscribers: add fields, never rename or remove them.
/// </summary>
public static class OutboxMapper
{
    public const string AppointmentCreated = "appointment.created";
    public const string AppointmentCancelled = "appointment.cancelled";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Null for events that are not published outside the API.</summary>
    public static OutboxMessage? ToMessage(IDomainEvent domainEvent) => domainEvent switch
    {
        AppointmentBooked e => Create(AppointmentCreated, e.OccurredAt, new
        {
            e.AppointmentId,
            e.CustomerId,
            e.ProfessionalId,
            e.ServiceId,
            e.StartsAt,
            e.EndsAt,
            e.Price,
        }),
        AppointmentCancelled e => Create(AppointmentCancelled, e.OccurredAt, new
        {
            e.AppointmentId,
            e.CustomerId,
            e.ProfessionalId,
            e.StartsAt,
            CancelledBy = e.ByAdmin ? "admin" : "customer",
            e.Reason,
        }),
        _ => null,
    };

    private static OutboxMessage Create(string type, DateTimeOffset occurredAt, object payload) =>
        new(Guid.CreateVersion7(), type, JsonSerializer.Serialize(payload, Json), occurredAt);
}
