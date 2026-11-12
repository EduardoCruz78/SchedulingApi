using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Api.Extensions;
using Scheduling.Application.Common;
using Scheduling.Application.Services;

namespace Scheduling.Api.Controllers;

[Route("api/services")]
[Tags("Services")]
public sealed class ServicesController : ApiController
{
    /// <summary>Lists the catalog. Only active services, unless an admin asks for inactive ones too.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedList<ServiceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedList<ServiceDto>>> List(
        [FromServices] ListServicesHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null) =>
        Ok(await handler.HandleAsync(includeInactive && CurrentUserIsAdmin, new PageRequest(page, pageSize), cancellationToken));

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceDto>> Get(Guid id, [FromServices] GetServiceHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ServiceDto>> Create(
        SaveServiceCommand request, [FromServices] CreateServiceHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : Failure(result.Error);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ServiceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServiceDto>> Update(
        Guid id, SaveServiceCommand request, [FromServices] UpdateServiceHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Deactivates the service (R12). It disappears from the public catalog; past appointments keep it.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Deactivate(Guid id, [FromServices] DeactivateServiceHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(id, cancellationToken));
}
