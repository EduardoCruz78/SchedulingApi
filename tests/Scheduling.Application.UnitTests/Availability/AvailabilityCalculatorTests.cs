using Scheduling.Application.Availability;
using Scheduling.Domain.Appointments;
using Scheduling.Domain.Common;
using Scheduling.Domain.Professionals;
using Scheduling.Domain.Services;
using Scheduling.Tests.Common;

namespace Scheduling.Application.UnitTests.Availability;

public class AvailabilityCalculatorTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly SchedulingPolicy Policy = SchedulingPolicy.Default;

    /// <summary>Tuesday. "Now" (Monday 12:00 UTC) is the day before, so lead-time rules do not interfere.</summary>
    private static readonly DateOnly Tuesday = new(2026, 12, 8);

    private static readonly Service ThirtyMinutes = new ServiceBuilder().WithDuration(30).Build();
    private static readonly Service OneHour = new ServiceBuilder().WithDuration(60).Build();

    private static IReadOnlyList<TimeSlot> Slots(
        Professional professional,
        Service service,
        DateOnly? date = null,
        IEnumerable<Appointment>? appointments = null,
        DateTimeOffset? now = null,
        TimeZoneInfo? zone = null) =>
        AvailabilityCalculator.GetFreeSlots(
            professional,
            service,
            date ?? Tuesday,
            appointments ?? [],
            now ?? TestTime.Now,
            zone ?? SaoPaulo,
            Policy);

    /// <summary>Local São Paulo time (UTC-3) on <see cref="Tuesday"/>, as UTC.</summary>
    private static DateTimeOffset Local(int hour, int minute = 0) =>
        new DateTimeOffset(2026, 12, 8, hour, minute, 0, TimeSpan.FromHours(-3)).ToUniversalTime();

    private static IEnumerable<string> LocalStarts(IEnumerable<TimeSlot> slots, TimeZoneInfo? zone = null) =>
        slots.Select(s => TimeZoneInfo.ConvertTime(s.StartsAt, zone ?? SaoPaulo).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void DayWithoutWorkingHours_ReturnsNoSlots()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Monday, "09:00", "18:00").Build();

        Slots(professional, ThirtyMinutes).Should().BeEmpty();
    }

    [Fact]
    public void SingleInterval_ReturnsEveryGridStepWhereTheServiceFits()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:00", "11:00").Build();

        var slots = Slots(professional, ThirtyMinutes);

        LocalStarts(slots).Should().Equal("09:00", "09:15", "09:30", "09:45", "10:00", "10:15", "10:30");
    }

    [Fact]
    public void Slots_AreReturnedInUtc()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:00", "10:00").Build();

        var first = Slots(professional, ThirtyMinutes)[0];

        first.StartsAt.Should().Be(TestTime.At(2026, 12, 8, 12)); // 09:00 in São Paulo = 12:00 UTC
        first.StartsAt.Offset.Should().Be(TimeSpan.Zero);
        first.EndsAt.Should().Be(TestTime.At(2026, 12, 8, 12, 30));
    }

    [Fact]
    public void SplitShift_SkipsTheLunchBreak()
    {
        var professional = new ProfessionalBuilder()
            .Working(DayOfWeek.Tuesday, "09:00", "11:00")
            .Working(DayOfWeek.Tuesday, "13:00", "15:00")
            .Build();

        var slots = Slots(professional, OneHour);

        LocalStarts(slots).Should().Equal("09:00", "09:15", "09:30", "09:45", "10:00", "13:00", "13:15", "13:30", "13:45", "14:00");
    }

    [Fact]
    public void ServiceLongerThanInterval_ReturnsNoSlots()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:00", "09:45").Build();

        Slots(professional, OneHour).Should().BeEmpty();
    }

    [Fact]
    public void ServiceExactlyAsLongAsInterval_ReturnsOneSlot()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:00", "10:00").Build();

        LocalStarts(Slots(professional, OneHour)).Should().Equal("09:00");
    }

    [Fact]
    public void IntervalStartingOffTheGrid_FirstSlotIsTheNextGridTime()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:10", "10:00").Build();

        LocalStarts(Slots(professional, ThirtyMinutes)).Should().Equal("09:15", "09:30");
    }

    [Fact]
    public void ExistingAppointment_RemovesEveryOverlappingCandidate()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:00", "12:00").Build();
        var booked = new AppointmentBuilder()
            .WithProfessional(professional.Id)
            .WithService(ThirtyMinutes)
            .StartingAt(Local(10))
            .Build(); // 10:00-10:30

        var slots = Slots(professional, OneHour, appointments: [booked]);

        // 60-minute slots that would touch 10:00-10:30 are gone; 09:00 (ends 10:00) and 10:30 remain.
        LocalStarts(slots).Should().Equal("09:00", "10:30", "10:45", "11:00");
    }

    [Fact]
    public void CancelledAppointment_DoesNotBlockTheSlot()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:00", "10:00").Build();
        var cancelled = new AppointmentBuilder()
            .WithProfessional(professional.Id)
            .WithService(OneHour)
            .StartingAt(Local(9))
            .Cancelled()
            .Build();

        LocalStarts(Slots(professional, OneHour, appointments: [cancelled])).Should().Equal("09:00");
    }

    [Fact]
    public void AppointmentOfAnotherProfessional_DoesNotBlockTheSlot()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:00", "10:00").Build();
        var otherProfessionalsAppointment = new AppointmentBuilder()
            .WithProfessional(Guid.NewGuid())
            .WithService(OneHour)
            .StartingAt(Local(9))
            .Build();

        Slots(professional, OneHour, appointments: [otherProfessionalsAppointment]).Should().HaveCount(1);
    }

    [Fact]
    public void TimeOffCoveringTheWholeDay_ReturnsNoSlots()
    {
        var professional = new ProfessionalBuilder()
            .Working(DayOfWeek.Tuesday, "09:00", "18:00")
            .WithTimeOff(Local(0), Local(0).AddDays(1))
            .Build();

        Slots(professional, ThirtyMinutes).Should().BeEmpty();
    }

    [Fact]
    public void PartialTimeOff_RemovesOnlyThatPeriod()
    {
        var professional = new ProfessionalBuilder()
            .Working(DayOfWeek.Tuesday, "09:00", "11:00")
            .WithTimeOff(Local(9, 30), Local(10, 30))
            .Build();

        LocalStarts(Slots(professional, ThirtyMinutes)).Should().Equal("09:00", "10:30");
    }

    [Fact]
    public void SlotsBeforeMinimumLeadTime_AreRemoved()
    {
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Tuesday, "09:00", "12:00").Build();
        var now = Local(9, 20); // first allowed start is 10:20, so 10:30 on the grid

        LocalStarts(Slots(professional, ThirtyMinutes, now: now)).Should().Equal("10:30", "10:45", "11:00", "11:15", "11:30");
    }

    [Fact]
    public void DayInThePast_ReturnsNoSlots()
    {
        var professional = new ProfessionalBuilder().WorkingWeekdays().Build();

        Slots(professional, ThirtyMinutes, date: new DateOnly(2026, 12, 1)).Should().BeEmpty();
    }

    [Fact]
    public void DayBeyondMaximumAdvance_ReturnsNoSlots()
    {
        var professional = new ProfessionalBuilder().WorkingWeekdays().Build();

        Slots(professional, ThirtyMinutes, date: Tuesday.AddDays(70)).Should().BeEmpty();
    }

    [Fact]
    public void SpringForward_SkipsNonExistentTimesAndUsesRealElapsedTime()
    {
        // New York, 2027-03-14: clocks jump from 02:00 EST to 03:00 EDT.
        var sunday = new DateOnly(2027, 3, 14);
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Sunday, "01:00", "04:00").Build();
        var now = TestTime.At(2027, 3, 1, 12);

        var slots = Slots(professional, OneHour, date: sunday, now: now, zone: NewYork);

        // 01:00-04:00 local is only 2 real hours (06:00Z-08:00Z). 02:xx does not exist.
        slots.Select(s => s.StartsAt).Should().Equal(
            TestTime.At(2027, 3, 14, 6),
            TestTime.At(2027, 3, 14, 6, 15),
            TestTime.At(2027, 3, 14, 6, 30),
            TestTime.At(2027, 3, 14, 6, 45),
            TestTime.At(2027, 3, 14, 7));
        LocalStarts(slots, NewYork).Should().Equal("01:00", "01:15", "01:30", "01:45", "03:00");
    }

    [Fact]
    public void FallBack_ReturnsOnlyDistinctSlotsInsideTheRealWorkingTime()
    {
        // New York, 2027-11-07: clocks go back from 02:00 EDT to 01:00 EST, so 01:xx happens twice.
        var sunday = new DateOnly(2027, 11, 7);
        var professional = new ProfessionalBuilder().Working(DayOfWeek.Sunday, "00:00", "03:00").Build();
        var now = TestTime.At(2027, 11, 1, 12);
        var workStart = TestTime.At(2027, 11, 7, 4);  // 00:00 EDT
        var workEnd = TestTime.At(2027, 11, 7, 8);    // 03:00 EST

        var slots = Slots(professional, OneHour, date: sunday, now: now, zone: NewYork);

        slots.Should().NotBeEmpty();
        slots.Select(s => s.StartsAt).Should().OnlyHaveUniqueItems().And.BeInAscendingOrder();
        slots.Should().OnlyContain(s => s.StartsAt >= workStart && s.EndsAt <= workEnd);
        slots[^1].StartsAt.Should().Be(TestTime.At(2027, 11, 7, 7)); // 02:00 EST, ends exactly at 03:00 EST
    }
}
