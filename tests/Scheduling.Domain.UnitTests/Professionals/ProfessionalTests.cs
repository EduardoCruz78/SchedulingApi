using Scheduling.Domain.Professionals;
using Scheduling.Tests.Common;

namespace Scheduling.Domain.UnitTests.Professionals;

public class ProfessionalTests
{
    private static WorkingHours Hours(DayOfWeek day, int startHour, int endHour) =>
        new(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    [Fact]
    public void SetWorkingHours_WithSplitShift_KeepsBothIntervals()
    {
        var professional = Professional.Create("Ana").Value;

        var result = professional.SetWorkingHours([Hours(DayOfWeek.Monday, 13, 18), Hours(DayOfWeek.Monday, 9, 12)]);

        result.IsSuccess.Should().BeTrue();
        professional.WorkingHoursOn(DayOfWeek.Monday).Select(h => h.Start)
            .Should().Equal(new TimeOnly(9, 0), new TimeOnly(13, 0));
    }

    [Fact]
    public void SetWorkingHours_WithOverlappingIntervalsOnSameDay_Fails()
    {
        var professional = Professional.Create("Ana").Value;

        var result = professional.SetWorkingHours([Hours(DayOfWeek.Monday, 9, 13), Hours(DayOfWeek.Monday, 12, 18)]);

        result.Error.Should().Be(ProfessionalErrors.OverlappingWorkingHours);
    }

    [Fact]
    public void SetWorkingHours_WithBackToBackIntervals_Succeeds()
    {
        var professional = Professional.Create("Ana").Value;

        var result = professional.SetWorkingHours([Hours(DayOfWeek.Monday, 9, 12), Hours(DayOfWeek.Monday, 12, 18)]);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void SetWorkingHours_WithSameHoursOnDifferentDays_Succeeds()
    {
        var professional = Professional.Create("Ana").Value;

        var result = professional.SetWorkingHours([Hours(DayOfWeek.Monday, 9, 18), Hours(DayOfWeek.Tuesday, 9, 18)]);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(18, 9)]
    [InlineData(9, 9)]
    public void SetWorkingHours_WithStartNotBeforeEnd_Fails(int start, int end)
    {
        var professional = Professional.Create("Ana").Value;

        var result = professional.SetWorkingHours([Hours(DayOfWeek.Monday, start, end)]);

        result.Error.Should().Be(ProfessionalErrors.InvalidWorkingHoursInterval);
    }

    [Fact]
    public void SetWorkingHours_WhenInvalid_KeepsPreviousSchedule()
    {
        var professional = Professional.Create("Ana").Value;
        professional.SetWorkingHours([Hours(DayOfWeek.Monday, 9, 18)]);

        professional.SetWorkingHours([Hours(DayOfWeek.Monday, 18, 9)]);

        professional.WorkingHours.Should().ContainSingle().Which.Should().Be(Hours(DayOfWeek.Monday, 9, 18));
    }

    [Fact]
    public void SetServices_IgnoresDuplicatesAndReplacesPreviousList()
    {
        var professional = Professional.Create("Ana").Value;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        professional.SetServices([a]);

        professional.SetServices([b, b]);

        professional.ServiceIds.Should().Equal(b);
        professional.Performs(a).Should().BeFalse();
    }

    [Fact]
    public void AddTimeOff_WithValidPeriod_IsReturnedAndBlocksThatPeriod()
    {
        var professional = Professional.Create("Ana").Value;
        var start = TestTime.Now.AddDays(1);

        var result = professional.AddTimeOff(start, start.AddDays(2), "  Vacation ", TestTime.Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Reason.Should().Be("Vacation");
        professional.IsOnTimeOff(start.AddHours(5), start.AddHours(6)).Should().BeTrue();
        professional.IsOnTimeOff(start.AddDays(2), start.AddDays(2).AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void AddTimeOff_EndingInThePast_Fails()
    {
        var professional = Professional.Create("Ana").Value;

        var result = professional.AddTimeOff(TestTime.Now.AddDays(-2), TestTime.Now.AddDays(-1), null, TestTime.Now);

        result.Error.Should().Be(ProfessionalErrors.TimeOffInThePast);
    }

    [Fact]
    public void AddTimeOff_WithEndBeforeStart_Fails()
    {
        var professional = Professional.Create("Ana").Value;

        var result = professional.AddTimeOff(TestTime.Now.AddDays(2), TestTime.Now.AddDays(1), null, TestTime.Now);

        result.Error.Should().Be(ProfessionalErrors.InvalidTimeOffInterval);
    }

    [Fact]
    public void AddTimeOff_OverlappingExistingTimeOff_Fails()
    {
        var professional = Professional.Create("Ana").Value;
        professional.AddTimeOff(TestTime.Now.AddDays(1), TestTime.Now.AddDays(3), null, TestTime.Now);

        var result = professional.AddTimeOff(TestTime.Now.AddDays(2), TestTime.Now.AddDays(4), null, TestTime.Now);

        result.Error.Should().Be(ProfessionalErrors.OverlappingTimeOff);
    }

    [Fact]
    public void RemoveTimeOff_UnknownId_ReturnsNotFound()
    {
        var professional = Professional.Create("Ana").Value;

        professional.RemoveTimeOff(Guid.NewGuid()).Error.Should().Be(ProfessionalErrors.TimeOffNotFound);
    }

    [Fact]
    public void RemoveTimeOff_ExistingId_FreesThePeriod()
    {
        var professional = Professional.Create("Ana").Value;
        var timeOff = professional.AddTimeOff(TestTime.Now.AddDays(1), TestTime.Now.AddDays(2), null, TestTime.Now).Value;

        professional.RemoveTimeOff(timeOff.Id).IsSuccess.Should().BeTrue();

        professional.TimeOffs.Should().BeEmpty();
    }
}
