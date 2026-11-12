using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Scheduling.Application.Abstractions;
using Scheduling.Domain.Users;

namespace Scheduling.Infrastructure.Auth;

/// <summary>JWT settings, bound from the "Jwt" section. The secret comes from environment variables or user-secrets, never from source control.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = "scheduling-api";

    [Required]
    public string Audience { get; init; } = "scheduling-api";

    /// <summary>HMAC-SHA256 key. At least 32 bytes (256 bits).</summary>
    [Required]
    [MinLength(32)]
    public string Secret { get; init; } = string.Empty;

    [Range(5, 24 * 60)]
    public int ExpirationMinutes { get; init; } = 60;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Secret));
}

/// <summary>Short claim names used in tokens and by the API's authorization.</summary>
public static class TokenClaims
{
    public const string Role = "role";
    public const string Name = "name";
}

/// <summary>Issues short-lived access tokens signed with HMAC-SHA256.</summary>
internal sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public AuthToken CreateAccessToken(User user)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(settings.ExpirationMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.SigningKey(), SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(TokenClaims.Name, user.Name),
                new Claim(TokenClaims.Role, user.Role.ToString()),
            ]),
        };

        return new AuthToken(_handler.CreateToken(descriptor), expiresAt);
    }
}
