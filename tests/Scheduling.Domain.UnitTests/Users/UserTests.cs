using Scheduling.Domain.Users;
using Scheduling.Tests.Common;

namespace Scheduling.Domain.UnitTests.Users;

public class UserTests
{
    [Fact]
    public void Create_NormalizesEmailToLowerCase()
    {
        var user = User.Create("Maria", "  Maria@Example.COM ", "hash", Role.Customer, TestTime.Now).Value;

        user.Email.Should().Be("maria@example.com");
        user.IsAdmin.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("maria@")]
    [InlineData("Maria <maria@example.com>")]
    public void Create_WithInvalidEmail_Fails(string email)
    {
        User.Create("Maria", email, "hash", Role.Customer, TestTime.Now).Error.Should().Be(UserErrors.InvalidEmail);
    }

    [Fact]
    public void Create_WithoutName_Fails()
    {
        User.Create(" ", "maria@example.com", "hash", Role.Customer, TestTime.Now).Error.Should().Be(UserErrors.InvalidName);
    }
}
