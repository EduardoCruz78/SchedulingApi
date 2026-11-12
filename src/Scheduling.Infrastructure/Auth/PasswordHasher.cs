using Microsoft.AspNetCore.Identity;
using Scheduling.Application.Abstractions;

namespace Scheduling.Infrastructure.Auth;

/// <summary>
/// Adapter over ASP.NET Core Identity's hasher (PBKDF2 with HMAC-SHA512, 100k iterations, random salt).
/// Using the framework implementation avoids hand-rolled crypto and keeps hashes upgradeable.
/// </summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string password) => _inner.HashPassword(Subject, password);

    public bool Verify(string hashedPassword, string providedPassword) =>
        _inner.VerifyHashedPassword(Subject, hashedPassword, providedPassword) != PasswordVerificationResult.Failed;
}
