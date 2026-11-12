using FluentValidation;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Common;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;

namespace Scheduling.Application.Auth;

public sealed record UserDto(Guid Id, string Name, string Email, string Role, DateTimeOffset CreatedAt)
{
    public static UserDto From(User user) => new(user.Id, user.Name, user.Email, user.Role.ToString(), user.CreatedAt);
}

public static class AuthErrors
{
    public static readonly Error EmailTaken =
        Error.Conflict("Auth.EmailTaken", "An account with this e-mail already exists.");

    /// <summary>Same error for unknown e-mail and wrong password, so the API does not reveal which e-mails exist.</summary>
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "E-mail or password is incorrect.");

    public static readonly Error UserNotFound =
        Error.NotFound("Auth.UserNotFound", "User not found.");
}

// ---------- Register ----------

public sealed record RegisterCommand(string Name, string Email, string Password);

public sealed class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(c => c.Email).NotEmpty().MaximumLength(User.EmailMaxLength).EmailAddress();
        RuleFor(c => c.Password).StrongPassword();
    }
}

public sealed class RegisterHandler(
    IValidator<RegisterCommand> validator,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider)
{
    public async Task<Result<UserDto>> HandleAsync(RegisterCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var email = User.NormalizeEmail(command.Email);
        if (email is null)
        {
            return UserErrors.InvalidEmail;
        }

        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            return AuthErrors.EmailTaken;
        }

        var created = User.Create(command.Name, email, passwordHasher.Hash(command.Password), Role.Customer, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        users.Add(created.Value);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException ex) when (ex.Kind == ConflictKind.Duplicate)
        {
            // Two registrations with the same e-mail at the same time: the unique index decides.
            return AuthErrors.EmailTaken;
        }

        return UserDto.From(created.Value);
    }
}

// ---------- Login ----------

public sealed record LoginCommand(string Email, string Password);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(c => c.Email).NotEmpty();
        RuleFor(c => c.Password).NotEmpty();
    }
}

public sealed class LoginHandler(
    IValidator<LoginCommand> validator,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService)
{
    private readonly Lazy<string> _dummyHash = new(() => passwordHasher.Hash("dummy-password-for-timing"));

    public async Task<Result<AccessToken>> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var email = User.NormalizeEmail(command.Email);
        var user = email is null ? null : await users.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            // Spend the same time as a real check so response timing does not reveal registered e-mails.
            passwordHasher.Verify(_dummyHash.Value, command.Password);
            return AuthErrors.InvalidCredentials;
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            return AuthErrors.InvalidCredentials;
        }

        return tokenService.CreateAccessToken(user);
    }
}

// ---------- Me ----------

public sealed class GetCurrentUserHandler(IUserRepository users)
{
    public async Task<Result<UserDto>> HandleAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null ? AuthErrors.UserNotFound : UserDto.From(user);
    }
}
