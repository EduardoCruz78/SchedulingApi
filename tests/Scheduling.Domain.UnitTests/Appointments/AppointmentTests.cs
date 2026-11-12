using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Tests.Common;

namespace Scheduling.Domain.UnitTests.Appointments;

public class AppointmentTests
{
    private static readonly SchedulingPolicy Policy = SchedulingPolicy.Default;
    private static readonly DateTimeOffset Now = TestTime.Now;

    private static Result<Appointment> Book(DateTimeOffset startsAt, int durationMinutes = 30, decimal price = 50m)
    {
        var service = new ServiceBuilder().WithDuration(durationMinutes).WithPrice(price).Build();
        return Appointment.Book(Guid.NewGuid(), Guid.NewGuid(), service, startsAt, Now, Policy);
    }

    // R8
    [Fact]
    public void Book_SetsEndFromServiceDurationAndCopiesPrice()
    {
        var startsAt = Now.AddDays(1);

        var appointment = Book(startsAt, durationMinutes: 45, price: 80m).Value;

        appointment.EndsAt.Should().Be(startsAt.AddMinutes(45));
        appointment.PriceAtBooking.Should().Be(80m);
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Book_WithNonUtcOffset_StoresUtc()
    {
        var local = new DateTimeOffset(2026, 12, 8, 10, 0, 0, TimeSpan.FromHours(-3));

        var appointment = Book(local).Value;

        appointment.StartsAt.Offset.Should().Be(TimeSpan.Zero);
        appointment.StartsAt.Should().Be(TestTime.At(2026, 12, 8, 13));
    }

    // R6
    [Theory]
    [InlineData(10)]
    [InlineData(1)]
    public void Book_OffTheGrid_Fails(int minutes)
    {
        var result = Book(Now.AddDays(1).AddMinutes(minutes));

        result.Error!.Code.Should().Be("Appointment.NotOnGrid");
    }

    [Fact]
    public void Book_WithSeconds_IsOffTheGrid()
    {
        Book(Now.AddDays(1).AddSeconds(30)).Error!.Code.Should().Be("Appointment.NotOnGrid");
    }

    // R5
    [Fact]
    public void Book_LessThanMinimumLeadTime_Fails()
    {
        Book(Now.AddMinutes(45)).Error!.Code.Should().Be("Appointment.TooSoon");
    }

    [Fact]
    public void Book_ExactlyAtMinimumLeadTime_Succeeds()
    {
        Book(Now.AddHours(1)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Book_InThePast_Fails()
    {
        Book(Now.AddDays(-1)).Error!.Code.Should().Be("Appointment.TooSoon");
    }

    [Fact]
    public void Book_BeyondMaximumAdvance_Fails()
    {
        Book(Now.AddDays(60).AddMinutes(15)).Error!.Code.Should().Be("Appointment.TooFarAhead");
    }

    [Fact]
    public void Book_ExactlyAtMaximumAdvance_Succeeds()
    {
        Book(Now.AddDays(60)).IsSuccess.Should().BeTrue();
    }

    // R9
    [Fact]
    public void Cancel_ByCustomerBeforeWindow_Succeeds()
    {
        var appointment = Book(Now.AddDays(1)).Value;

        var result = appointment.Cancel(byAdmin: false, " Changed plans ", Now, Policy);

        result.IsSuccess.Should().BeTrue();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.CancelledAt.Should().Be(Now);
        appointment.CancellationReason.Should().Be("Changed plans");
        appointment.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Cancel_ByCustomerInsideWindow_Fails()
    {
        var appointment = Book(Now.AddHours(3)).Value;

        var result = appointment.Cancel(byAdmin: false, null, Now.AddHours(1).AddMinutes(15), Policy);

        result.Error!.Code.Should().Be("Appointment.CancellationWindowClosed");
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public void Cancel_ByAdminInsideWindow_Succeeds()
    {
        var appointment = Book(Now.AddHours(3)).Value;

        appointment.Cancel(byAdmin: true, null, Now.AddHours(2).AddMinutes(30), Policy).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Cancel_Twice_Fails()
    {
        var appointment = Book(Now.AddDays(1)).Value;
        appointment.Cancel(byAdmin: false, null, Now, Policy);

        appointment.Cancel(byAdmin: true, null, Now, Policy).Error!.Code.Should().Be("Appointment.InvalidTransition");
    }

    [Fact]
    public void Cancel_WithReasonTooLong_Fails()
    {
        var appointment = Book(Now.AddDays(1)).Value;

        var result = appointment.Cancel(false, new string('x', Appointment.CancellationReasonMaxLength + 1), Now, Policy);

        result.Error.Should().Be(AppointmentErrors.InvalidCancellationReason);
    }

    // R10
    [Fact]
    public void Complete_AfterStart_Succeeds()
    {
        var appointment = Book(Now.AddDays(1)).Value;

        appointment.Complete(appointment.StartsAt.AddMinutes(10)).IsSuccess.Should().BeTrue();

        appointment.Status.Should().Be(AppointmentStatus.Completed);
    }

    [Fact]
    public void Complete_BeforeStart_Fails()
    {
        var appointment = Book(Now.AddDays(1)).Value;

        appointment.Complete(appointment.StartsAt.AddMinutes(-1)).Error.Should().Be(AppointmentErrors.NotStartedYet);
    }

    [Fact]
    public void MarkNoShow_AfterStart_Succeeds()
    {
        var appointment = Book(Now.AddDays(1)).Value;

        appointment.MarkNoShow(appointment.StartsAt).IsSuccess.Should().BeTrue();

        appointment.Status.Should().Be(AppointmentStatus.NoShow);
        appointment.IsActive.Should().BeTrue("a no-show still occupied the professional's time");
    }

    [Fact]
    public void FinalStates_CannotChange()
    {
        var appointment = Book(Now.AddDays(1)).Value;
        var later = appointment.StartsAt.AddHours(1);
        appointment.Complete(later);

        appointment.MarkNoShow(later).Error!.Code.Should().Be("Appointment.InvalidTransition");
        appointment.Cancel(byAdmin: true, null, later, Policy).Error!.Code.Should().Be("Appointment.InvalidTransition");
        appointment.Status.Should().Be(AppointmentStatus.Completed);
    }

    [Fact]
    public void Complete_CancelledAppointment_Fails()
    {
        var appointment = Book(Now.AddDays(1)).Value;
        appointment.Cancel(byAdmin: true, null, Now, Policy);

        appointment.Complete(appointment.StartsAt.AddHours(1)).Error!.Code.Should().Be("Appointment.InvalidTransition");
    }

    [Theory]
    [InlineData(0, 30, true)]   // same slot
    [InlineData(15, 30, true)]  // starts in the middle
    [InlineData(-15, 30, true)] // ends in the middle
    [InlineData(30, 30, false)] // back-to-back after
    [InlineData(-30, 30, false)] // back-to-back before
    public void Overlaps_UsesHalfOpenIntervals(int offsetMinutes, int durationMinutes, bool expected)
    {
        var appointment = Book(Now.AddDays(1)).Value; // 30 minutes
        var start = appointment.StartsAt.AddMinutes(offsetMinutes);

        appointment.Overlaps(start, start.AddMinutes(durationMinutes)).Should().Be(expected);
    }
}
