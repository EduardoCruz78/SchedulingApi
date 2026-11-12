using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;

namespace Scheduling.Tests.Common;

/// <summary>Fixed reference instants so tests never depend on the real clock.</summary>
public static class TestTime
{
    /// <summary>Monday, 2026-12-07 12:00 UTC (09:00 in São Paulo).</summary>
    public static readonly DateTimeOffset Now = new(2026, 12, 7, 12, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset At(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}

public sealed class ServiceBuilder
{
    private string _name = "Haircut";
    private string? _description = "Classic haircut";
    private int _durationMinutes = 30;
    private decimal _price = 50m;
    private bool _active = true;

    public ServiceBuilder WithName(string name) { _name = name; return this; }

    public ServiceBuilder WithDuration(int minutes) { _durationMinutes = minutes; return this; }

    public ServiceBuilder WithPrice(decimal price) { _price = price; return this; }

    public ServiceBuilder Inactive() { _active = false; return this; }

    public Service Build()
    {
        var service = Service.Create(_name, _description, _durationMinutes, _price).Value;
        if (!_active)
        {
            service.Deactivate();
        }

        return service;
    }
}

public sealed class ProfessionalBuilder
{
    private string _name = "Ana";
    private readonly List<Guid> _serviceIds = [];
    private readonly List<WorkingHours> _hours = [];
    private readonly List<(DateTimeOffset Start, DateTimeOffset End)> _timeOffs = [];
    private bool _active = true;

    public ProfessionalBuilder WithName(string name) { _name = name; return this; }

    public ProfessionalBuilder Performing(params Service[] services)
    {
        _serviceIds.AddRange(services.Select(s => s.Id));
        return this;
    }

    public ProfessionalBuilder Working(DayOfWeek day, string start, string end)
    {
        _hours.Add(new WorkingHours(day, TimeOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture)));
        return this;
    }

    /// <summary>Monday to Friday, 09:00-18:00 local time.</summary>
    public ProfessionalBuilder WorkingWeekdays(string start = "09:00", string end = "18:00")
    {
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            Working(day, start, end);
        }

        return this;
    }

    public ProfessionalBuilder WithTimeOff(DateTimeOffset start, DateTimeOffset end)
    {
        _timeOffs.Add((start, end));
        return this;
    }

    public ProfessionalBuilder Inactive() { _active = false; return this; }

    public Professional Build()
    {
        var professional = Professional.Create(_name).Value;
        professional.SetServices(_serviceIds);

        var hours = professional.SetWorkingHours(_hours);
        if (hours.IsFailure)
        {
            throw new InvalidOperationException(hours.Error.Message);
        }

        foreach (var (start, end) in _timeOffs)
        {
            // Use a "now" far in the past so builders can create time off at any instant.
            var added = professional.AddTimeOff(start, end, null, DateTimeOffset.UnixEpoch);
            if (added.IsFailure)
            {
                throw new InvalidOperationException(added.Error.Message);
            }
        }

        if (!_active)
        {
            professional.Deactivate();
        }

        return professional;
    }
}

public sealed class AppointmentBuilder
{
    private Guid _customerId = Guid.NewGuid();
    private Guid _professionalId = Guid.NewGuid();
    private Service _service = new ServiceBuilder().Build();
    private DateTimeOffset _startsAt = TestTime.Now.AddDays(1);
    private AppointmentStatus _status = AppointmentStatus.Scheduled;

    public AppointmentBuilder ForCustomer(Guid customerId) { _customerId = customerId; return this; }

    public AppointmentBuilder WithProfessional(Guid professionalId) { _professionalId = professionalId; return this; }

    public AppointmentBuilder WithService(Service service) { _service = service; return this; }

    public AppointmentBuilder StartingAt(DateTimeOffset startsAt) { _startsAt = startsAt; return this; }

    public AppointmentBuilder Cancelled() { _status = AppointmentStatus.Cancelled; return this; }

    public Appointment Build()
    {
        // Book "one day before" so any start time passes the lead-time rules.
        var bookedAt = _startsAt.AddDays(-1);
        var appointment = Appointment.Book(_customerId, _professionalId, _service, _startsAt, bookedAt, SchedulingPolicy.Default).Value;

        if (_status == AppointmentStatus.Cancelled)
        {
            appointment.Cancel(byAdmin: true, "test", bookedAt, SchedulingPolicy.Default);
        }

        return appointment;
    }
}
