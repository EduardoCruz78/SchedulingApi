using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scheduling.Domain.Users;
using Scheduling.Infrastructure.Auth;

namespace Scheduling.Api.Extensions;

public static class Policies
{
    public const string Admin = nameof(Admin);
    public const string Customer = nameof(Customer);
    public const string AuthRateLimit = "auth";
}

/// <summary>Limits for the anonymous auth endpoints, bound from "RateLimiting".</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [Range(1, 10_000)]
    public int AuthPermitLimit { get; init; } = 10;

    [Range(1, 3600)]
    public int AuthWindowSeconds { get; init; } = 60;
}

public static class SecurityExtensions
{
    public static IServiceCollection AddApiSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwt, timeProvider) =>
            {
                var settings = jwt.Value;
                var clockSkew = TimeSpan.FromSeconds(30);

                // Keep "sub", "role", "name" as they are instead of mapping them to long WS-* claim URIs.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = settings.SigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = TokenClaims.Name,
                    RoleClaimType = TokenClaims.Role,
                    ClockSkew = clockSkew,

                    // Expiry is checked against the application's TimeProvider, not the machine clock,
                    // so tests can move time forward and the whole app agrees on "now".
                    LifetimeValidator = (notBefore, expires, _, _) =>
                    {
                        var now = timeProvider.GetUtcNow().UtcDateTime;
                        return (notBefore is null || notBefore.Value <= now + clockSkew)
                            && (expires is null || expires.Value > now - clockSkew);
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(Role.Admin)))
            .AddPolicy(Policies.Customer, p => p.RequireRole(nameof(Role.Customer)));

        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Fixed window per client IP on login/register: slows down credential stuffing and sign-up spam.
            limiter.AddPolicy(Policies.AuthRateLimit, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limits.AuthPermitLimit,
                    Window = TimeSpan.FromSeconds(limits.AuthWindowSeconds),
                    QueueLimit = 0,
                });
            });

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "Too many attempts. Wait a moment and try again.",
                    },
                });
            };
        });

        return services;
    }
}
