using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Scheduling.Api.Contracts;
using Scheduling.Api.Extensions;
using Scheduling.Application.Abstractions;
using Scheduling.Application.Auth;

namespace Scheduling.Api.Controllers;

[Route("api/auth")]
[Tags("Auth")]
public sealed class AuthController : ApiController
{
    /// <summary>Creates a customer account.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(Policies.AuthRateLimit)]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UserDto>> Register(
        RegisterRequest request, [FromServices] RegisterHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Me), null, result.Value)
            : Failure(result.Error);
    }

    /// <summary>Exchanges e-mail and password for a JWT access token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(Policies.AuthRateLimit)]
    [ProducesResponseType<AuthToken>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthToken>> Login(
        LoginRequest request, [FromServices] LoginHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(request.ToCommand(), cancellationToken));

    /// <summary>Returns the authenticated user.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me([FromServices] GetCurrentUserHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(CurrentUserId, cancellationToken));
}
