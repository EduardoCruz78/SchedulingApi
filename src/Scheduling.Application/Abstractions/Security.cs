using Scheduling.Domain.Users;

namespace Scheduling.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hashedPassword, string providedPassword);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
}
