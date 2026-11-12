using Microsoft.Extensions.Options;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Domain.Common;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;

namespace Scheduling.Application.Availability;

/// <param name="StartsAt">UTC instant to send back when booking.</param>
/// <param name="EndsAt">UTC instant when the service would end.</param>
/// <param name="LocalStartsAt">Same instant in the business time zone, for display.</param>
public sealed record SlotDto(DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset LocalStartsAt);

public sealed record AvailabilityDto(Guid ProfessionalId, Guid ServiceId, DateOnly Date, string TimeZone, IReadOnlyList<SlotDto> Slots);

public sealed record GetAvailabilityQuery(Guid ProfessionalId, Guid ServiceId, DateOnly Date);

/// <summary>Loads what the pure <see cref="AvailabilityCalculator"/> needs and shapes its result for the API.</summary>
public sealed class GetAvailabilityHandler(
    IProfessionalRepository professionals,
    IServiceRepository services,
    IAppointmentRepository appointments,
    IOptions<BusinessOptions> businessOptions,
    IOptions<SchedulingOptions> schedulingOptions,
    TimeProvider timeProvider)
{
    public async Task<Result<AvailabilityDto>> HandleAsync(GetAvailabilityQuery query, CancellationToken cancellationToken = default)
    {
        var professional = await professionals.GetByIdAsync(query.ProfessionalId, cancellationToken);
        if (professional is null || !professional.IsActive)
        {
            return ProfessionalErrors.NotFound;
        }

        var service = await services.GetByIdAsync(query.ServiceId, cancellationToken);
        if (service is null)
        {
            return ServiceErrors.NotFound;
        }

        // R7
        if (!service.IsActive)
        {
            return ServiceErrors.Inactive;
        }

        if (!professional.Performs(service.Id))
        {
            return ProfessionalErrors.DoesNotPerformService;
        }

        var zone = businessOptions.Value.GetTimeZone();
        var dayStart = WorkingCalendar.ToUtc(query.Date, TimeOnly.MinValue, zone);
        var dayEnd = WorkingCalendar.ToUtc(query.Date.AddDays(1), TimeOnly.MinValue, zone);
        var booked = await appointments.ListActiveForProfessionalAsync(professional.Id, dayStart, dayEnd, cancellationToken);

        var slots = AvailabilityCalculator.GetFreeSlots(
            professional, service, query.Date, booked, timeProvider.GetUtcNow(), zone, schedulingOptions.Value.ToPolicy());

        return new AvailabilityDto(
            professional.Id,
            service.Id,
            query.Date,
            businessOptions.Value.TimeZone,
            slots.Select(s => new SlotDto(s.StartsAt, s.EndsAt, TimeZoneInfo.ConvertTime(s.StartsAt, zone))).ToList());
    }
}
