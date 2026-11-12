using System.Security.Claims;
using Scheduling.Domain.Common;
using Scheduling.Domain.Users;

namespace Scheduling.Api.Extensions;

public static class ErrorExtensions
{
    /// <summary>
    /// 400 malformed input · 401 not authenticated · 403 not allowed · 404 not found (or not yours)
    /// · 409 conflicting state · 422 well-formed but breaks a business rule.
    /// </summary>
    public static int ToStatusCode(this Error error) => error.Type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static string ToTitle(this Error error) => error.Type switch
    {
        ErrorType.Validation => "Invalid request",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        ErrorType.BusinessRule => "Business rule violated",
        _ => "Error",
    };
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var id)
            ? id
            : throw new InvalidOperationException("The authenticated user has no valid 'sub' claim.");

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(nameof(Role.Admin));
}
