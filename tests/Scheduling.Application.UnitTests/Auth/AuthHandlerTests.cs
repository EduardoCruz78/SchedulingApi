using Microsoft.Extensions.Time.Testing;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Auth;
using Scheduling.Application.UnitTests.Fakes;
using Scheduling.Domain.Users;
using Scheduling.Tests.Common;

namespace Scheduling.Application.UnitTests.Auth;

public class AuthHandlerTests
{
    private readonly InMemoryUsers _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakePasswordHasher _hasher = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RegisterHandler Register() =>
        new(new RegisterValidator(), _users, _unitOfWork, _hasher, new FakeTimeProvider(TestTime.Now));

    private LoginHandler Login() => new(new LoginValidator(), _users, _hasher, new FakeTokenService());

    [Fact]
    public async Task Register_CreatesCustomerWithHashedPassword()
    {
        var result = await Register().HandleAsync(new RegisterCommand("Maria", "Maria@Test.local", "Password123"), Ct);

        result.Value.Role.Should().Be("Customer");
        _users.Items.Single().PasswordHash.Should().Be("hash:Password123");
        _unitOfWork.SaveCount.Should().Be(1);
    }

    [Theory]
    [InlineData("short1")]
    [InlineData("onlyletters")]
    [InlineData("1234567890")]
    public async Task Register_WithWeakPassword_IsValidationError(string password)
    {
        var result = await Register().HandleAsync(new RegisterCommand("Maria", "maria@test.local", password), Ct);

        result.Error!.Fields.Should().ContainKey("password");
    }

    [Fact]
    public async Task Register_WithExistingEmail_IsConflict()
    {
        await Register().HandleAsync(new RegisterCommand("Maria", "maria@test.local", "Password123"), Ct);

        var result = await Register().HandleAsync(new RegisterCommand("Other", "MARIA@test.local", "Password123"), Ct);

        result.Error.Should().Be(AuthErrors.EmailTaken);
    }

    [Fact]
    public async Task Register_LosingARaceOnTheUniqueIndex_IsConflict()
    {
        _unitOfWork.FailNextSaveWith = ConflictKind.Duplicate;

        var result = await Register().HandleAsync(new RegisterCommand("Maria", "maria@test.local", "Password123"), Ct);

        result.Error.Should().Be(AuthErrors.EmailTaken);
    }

    [Fact]
    public async Task Login_WithRightPassword_ReturnsToken()
    {
        await Register().HandleAsync(new RegisterCommand("Maria", "maria@test.local", "Password123"), Ct);

        var result = await Login().HandleAsync(new LoginCommand("MARIA@test.local", "Password123"), Ct);

        result.Value.AccessToken.Should().StartWith("token-for-");
    }

    [Fact]
    public async Task Login_WithUnknownEmail_StillVerifiesAHash_AndReturnsTheSameError()
    {
        await Register().HandleAsync(new RegisterCommand("Maria", "maria@test.local", "Password123"), Ct);

        var wrongPassword = await Login().HandleAsync(new LoginCommand("maria@test.local", "Wrong1234"), Ct);
        var unknownEmail = await Login().HandleAsync(new LoginCommand("nobody@test.local", "Password123"), Ct);

        wrongPassword.Error.Should().Be(AuthErrors.InvalidCredentials);
        unknownEmail.Error.Should().Be(AuthErrors.InvalidCredentials);
        _hasher.VerifyCount.Should().Be(2, "both paths do the expensive hash check, so timing does not leak which e-mails exist");
    }

    [Fact]
    public async Task Me_ForDeletedUser_IsNotFound()
    {
        var result = await new GetCurrentUserHandler(_users).HandleAsync(Guid.NewGuid(), Ct);

        result.Error.Should().Be(AuthErrors.UserNotFound);
    }

    [Fact]
    public void Role_DefaultsAreNotAdmin()
    {
        User.Create("Maria", "maria@test.local", "hash", Role.Customer, TestTime.Now).Value.IsAdmin.Should().BeFalse();
    }
}
