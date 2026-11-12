using Scheduling.Domain.Services;

namespace Scheduling.Domain.UnitTests.Services;

public class ServiceTests
{
    [Fact]
    public void Create_WithValidData_IsActiveAndTrimmed()
    {
        var result = Service.Create("  Haircut  ", "  ", 45, 60m);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Haircut");
        result.Value.Description.Should().BeNull();
        result.Value.Duration.Should().Be(TimeSpan.FromMinutes(45));
        result.Value.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(495)]
    public void Create_WithInvalidDuration_Fails(int minutes)
    {
        var result = Service.Create("Haircut", null, minutes, 50m);

        result.Error.Should().Be(ServiceErrors.InvalidDuration);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(480)]
    public void Create_WithDurationAtTheLimits_Succeeds(int minutes)
    {
        Service.Create("Haircut", null, minutes, 50m).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_WithNegativePrice_Fails()
    {
        Service.Create("Haircut", null, 30, -0.01m).Error.Should().Be(ServiceErrors.InvalidPrice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutName_Fails(string name)
    {
        Service.Create(name, null, 30, 50m).Error.Should().Be(ServiceErrors.InvalidName);
    }

    [Fact]
    public void Create_WithNameLongerThanLimit_Fails()
    {
        Service.Create(new string('a', Service.NameMaxLength + 1), null, 30, 50m).Error.Should().Be(ServiceErrors.InvalidName);
    }

    [Fact]
    public void Update_WithInvalidData_KeepsPreviousValues()
    {
        var service = Service.Create("Haircut", null, 30, 50m).Value;

        var result = service.Update("Beard", null, 7, 20m);

        result.IsFailure.Should().BeTrue();
        service.Name.Should().Be("Haircut");
        service.DurationMinutes.Should().Be(30);
    }

    [Fact]
    public void Deactivate_ThenActivate_TogglesAvailability()
    {
        var service = Service.Create("Haircut", null, 30, 50m).Value;

        service.Deactivate();
        service.IsActive.Should().BeFalse();

        service.Activate();
        service.IsActive.Should().BeTrue();
    }
}
