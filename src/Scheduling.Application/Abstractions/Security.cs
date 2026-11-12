using Scheduling.Domain.Users;

namespace Scheduling.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hashedPassword, string providedPassword);
}

public sealed record AuthToken(string AccessToken, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AuthToken CreateAccessToken(User user);
}
