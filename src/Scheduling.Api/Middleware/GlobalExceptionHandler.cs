using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Application.Abstractions;

namespace Scheduling.Api.Middleware;

/// <summary>
/// Last line of defense: unexpected exceptions become a 500 ProblemDetails with a trace id and no stack trace.
/// Database conflicts that escaped a use case (e.g. two admins editing the same record) become 409.
/// </summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        if (exception is PersistenceConflictException conflict)
        {
            logger.LogWarning(exception, "Database conflict ({Kind}) on {Path}", conflict.Kind, httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = conflict.Kind == ConflictKind.Concurrency
                    ? "The resource was changed by another request. Reload it and try again."
                    : "The request conflicts with the current state of the resource.",
                Extensions = { ["code"] = $"Persistence.{conflict.Kind}" },
            };
        }
        else
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Internal server error",
                Detail = "An unexpected error occurred. Use the trace id when reporting it.",
            };
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
