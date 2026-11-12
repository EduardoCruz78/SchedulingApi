using Scheduling.Application.Availability;
using Scheduling.Tests.Common;

namespace Scheduling.Application.UnitTests.Availability;

/// <summary>R1: the appointment must fit inside a working interval of that local day.</summary>
public class WorkingCalendarTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static DateTimeOffset LocalTuesday(int hour, int minute = 0) =>
        new DateTimeOffset(2026, 12, 8, hour, minute, 0, TimeSpan.FromHours(-3)).ToUniversalTime();

    private static readonly Domain.Professionals.Professional SplitShift = new ProfessionalBuilder()
        .Working(DayOfWeek.Tuesday, "09:00", "12:00")
        .Working(DayOfWeek.Tuesday, "13:00", "18:00")
        .Build();

    [Theory]
    [InlineData(9, 0, 30, true)]    // first minute of the day
    [InlineData(11, 30, 30, true)]  // ends exactly when the morning shift ends
    [InlineData(11, 45, 30, false)] // crosses into the lunch break
    [InlineData(12, 15, 30, false)] // inside the lunch break
    [InlineData(8, 45, 30, false)]  // starts before opening
    [InlineData(17, 30, 30, true)]
    [InlineData(17, 45, 30, false)] // crosses closing time
    public void FitsWorkingHours_ChecksTheWholeInterval(int hour, int minute, int duration, bool expected)
    {
        var start = LocalTuesday(hour, minute);

        WorkingCalendar.FitsWorkingHours(SplitShift, start, start.AddMinutes(duration), SaoPaulo).Should().Be(expected);
    }

    [Fact]
    public void FitsWorkingHours_OnADayOff_IsFalse()
    {
        var wednesday = LocalTuesday(10).AddDays(1);

        WorkingCalendar.FitsWorkingHours(SplitShift, wednesday, wednesday.AddMinutes(30), SaoPaulo).Should().BeFalse();
    }

    [Fact]
    public void LocalDate_UsesTheBusinessTimeZone()
    {
        // 01:00 UTC on Wednesday is still 22:00 on Tuesday in São Paulo.
        var instant = TestTime.At(2026, 12, 9, 1);

        WorkingCalendar.LocalDate(instant, SaoPaulo).Should().Be(new DateOnly(2026, 12, 8));
    }
}
