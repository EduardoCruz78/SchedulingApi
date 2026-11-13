using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Tests.Common;

namespace Scheduling.Domain.UnitTests.Appointments;

/// <summary>The aggregate records what happened; the outbox publishes it (ADR 0005).</summary>
public class AppointmentEventsTests
{
    private static readonly SchedulingPolicy Policy = SchedulingPolicy.Default;
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static Appointment BookTomorrow()
    {
        var service = new ServiceBuilder().WithDuration(30).WithPrice(50m).Build();
        return Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), service, Now.AddDays(1), Now, Policy).Value;
    }

    [Fact]
    public void Book_RaisesAppointmentBooked_WithTheBookingDetails()
    {
        var appointment = BookTomorrow();

        appointment.GetDomainEvents().Should().ContainSingle().Which.Should().Be(new AppointmentBooked(
            appointment.Id,
            appointment.CustomerId,
            appointment.ProfessionalId,
            appointment.ServiceId,
            appointment.StartsAt,
            appointment.EndsAt,
            50m,
            Now));
    }

    [Fact]
    public void Book_WhenARuleFails_RaisesNothing()
    {
        var service = new ServiceBuilder().Build();

        var result = Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), service, Now.AddMinutes(10), Now, Policy);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Cancel_RaisesAppointmentCancelled_WithWhoCancelledAndWhy()
    {
        var appointment = BookTomorrow();
        appointment.ClearDomainEvents();

        appointment.Cancel(byAdmin: false, "  Sick  ", Now, Policy);

        appointment.GetDomainEvents().Should().ContainSingle().Which.Should().Be(new AppointmentCancelled(
            appointment.Id,
            appointment.CustomerId,
            appointment.ProfessionalId,
            appointment.StartsAt,
            ByAdmin: false,
            "Sick",
            Now));
    }

    [Fact]
    public void Cancel_WhenRefused_RaisesNothing()
    {
        var appointment = BookTomorrow();
        appointment.ClearDomainEvents();

        // R9: a customer cannot cancel inside the 2-hour window.
        var result = appointment.Cancel(byAdmin: false, null, appointment.StartsAt.AddMinutes(-30), Policy);

        result.IsFailure.Should().BeTrue();
        appointment.GetDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void CompleteAndNoShow_AreNotPublished()
    {
        var completed = BookTomorrow();
        var noShow = BookTomorrow();
        completed.ClearDomainEvents();
        noShow.ClearDomainEvents();

        completed.Complete(completed.StartsAt);
        noShow.MarkNoShow(noShow.StartsAt);

        completed.GetDomainEvents().Should().BeEmpty();
        noShow.GetDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheList()
    {
        var appointment = BookTomorrow();

        appointment.ClearDomainEvents();

        appointment.GetDomainEvents().Should().BeEmpty();
    }
}
