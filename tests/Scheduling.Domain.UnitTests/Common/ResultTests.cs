using Scheduling.Domain.Common;

namespace Scheduling.Domain.UnitTests.Common;

public class ResultTests
{
    [Fact]
    public void Value_OnFailure_Throws()
    {
        Result<int> result = Error.NotFound("X.NotFound", "Missing");

        var read = () => result.Value;

        read.Should().Throw<InvalidOperationException>().WithMessage("*X.NotFound*");
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }
}
