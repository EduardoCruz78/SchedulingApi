using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Scheduling.Api.Extensions;
using Scheduling.Domain.Common;

namespace Scheduling.Api.Controllers;

/// <summary>Base controller: converts <see cref="Result"/> failures into RFC 9457 ProblemDetails.</summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiController : ControllerBase
{
    protected Guid CurrentUserId => User.GetUserId();

    protected bool CurrentUserIsAdmin => User.IsAdmin();

    protected ActionResult<T> OkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? Ok(result.Value) : Failure(result.Error);

    protected ActionResult NoContentOrProblem(Result result) =>
        result.IsSuccess ? NoContent() : Failure(result.Error);

    /// <summary>Maps an <see cref="Error"/> to its HTTP status and a ProblemDetails body with the error code.</summary>
    protected ObjectResult Failure(Error error)
    {
        var status = error.ToStatusCode();

        if (error.Fields is { Count: > 0 })
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in error.Fields)
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError(field, message);
                }
            }

            var validation = ProblemDetailsFactory.CreateValidationProblemDetails(HttpContext, modelState, status, detail: error.Message);
            validation.Extensions["code"] = error.Code;
            return new ObjectResult(validation) { StatusCode = status };
        }

        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, title: error.ToTitle(), detail: error.Message);
        problem.Extensions["code"] = error.Code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
