using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Scheduling.Api.Extensions;
using Scheduling.Application.Availability;
using Scheduling.Application.Common;
using Scheduling.Application.Professionals;

namespace Scheduling.Api.Controllers;

[Route("api/professionals")]
[Tags("Professionals")]
public sealed class ProfessionalsController : ApiController
{
    /// <summary>Lists active professionals, optionally only those who perform a service.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedList<ProfessionalDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedList<ProfessionalDto>>> List(
        [FromServices] ListProfessionalsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? serviceId = null,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null) =>
        Ok(await handler.HandleAsync(serviceId, includeInactive && CurrentUserIsAdmin, new PageRequest(page, pageSize), cancellationToken));

    /// <summary>Public view hides inactive professionals and time off; admins see both.</summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<ProfessionalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfessionalDto>> Get(Guid id, [FromServices] GetProfessionalHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, CurrentUserIsAdmin, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ProfessionalDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfessionalDto>> Create(
        SaveProfessionalCommand request, [FromServices] CreateProfessionalHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : Failure(result.Error);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ProfessionalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfessionalDto>> Update(
        Guid id, SaveProfessionalCommand request, [FromServices] UpdateProfessionalHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Replaces the list of services the professional performs.</summary>
    [HttpPut("{id:guid}/services")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ProfessionalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProfessionalDto>> SetServices(
        Guid id, SetProfessionalServicesCommand request, [FromServices] SetProfessionalServicesHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Replaces the weekly schedule. Times are "HH:mm" in the business time zone (R11).</summary>
    [HttpPut("{id:guid}/working-hours")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ProfessionalDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfessionalDto>> SetWorkingHours(
        Guid id, SetWorkingHoursCommand request, [FromServices] SetWorkingHoursHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, request, cancellationToken));

    /// <summary>Blocks a period (vacation, day off). Refused if it would hide booked appointments (R13).</summary>
    [HttpPost("{id:guid}/time-off")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<TimeOffDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TimeOffDto>> AddTimeOff(
        Guid id, AddTimeOffCommand request, [FromServices] AddTimeOffHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id }, result.Value)
            : Failure(result.Error);
    }

    [HttpDelete("{id:guid}/time-off/{timeOffId:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveTimeOff(
        Guid id, Guid timeOffId, [FromServices] RemoveTimeOffHandler handler, CancellationToken cancellationToken) =>
        NoContentOrProblem(await handler.HandleAsync(id, timeOffId, cancellationToken));

    /// <summary>Free start times for a service on a date (business time zone). Send a slot's startsAt to book it.</summary>
    [HttpGet("{id:guid}/availability")]
    [AllowAnonymous]
    [ProducesResponseType<AvailabilityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AvailabilityDto>> Availability(
        Guid id,
        [FromQuery, BindRequired] Guid serviceId,
        [FromQuery, BindRequired] DateOnly date,
        [FromServices] GetAvailabilityHandler handler,
        CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(new GetAvailabilityQuery(id, serviceId, date), cancellationToken));
}
