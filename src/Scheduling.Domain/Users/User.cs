using System.Net.Mail;
using Scheduling.Domain.Common;

namespace Scheduling.Domain.Users;

public enum Role
{
    Customer,
    Admin,
}

public sealed class User : AggregateRoot
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;

    private User(Guid id, string name, string email, string passwordHash, Role role, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = createdAt;
    }

    private User()
    {
    }

    public string Name { get; private set; } = null!;

    /// <summary>Always trimmed and lower-case, so uniqueness checks are case-insensitive.</summary>
    public string Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public Role Role { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Result<User> Create(string name, string email, string passwordHash, Role role, DateTimeOffset now)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > NameMaxLength)
        {
            return UserErrors.InvalidName;
        }

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null)
        {
            return UserErrors.InvalidEmail;
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("A password hash is required.", nameof(passwordHash));
        }

        return new User(Guid.CreateVersion7(), name, normalizedEmail, passwordHash, role, now.ToUtc());
    }

    /// <summary>Returns the canonical form of an e-mail, or null when it is not a valid address.</summary>
    public static string? NormalizeEmail(string? email)
    {
        var candidate = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(candidate) || candidate.Length > EmailMaxLength)
        {
            return null;
        }

        return MailAddress.TryCreate(candidate, out var parsed) && parsed.Address == candidate
            ? candidate
            : null;
    }

    public bool IsAdmin => Role == Role.Admin;
}

public static class UserErrors
{
    public static readonly Error InvalidName =
        Error.Validation("User.InvalidName", $"Name is required and must have at most {User.NameMaxLength} characters.");

    public static readonly Error InvalidEmail =
        Error.Validation("User.InvalidEmail", "E-mail is not a valid address.");
}
