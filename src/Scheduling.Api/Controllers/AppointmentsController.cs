using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Scheduling.Api.Extensions;
using Scheduling.Application.Appointments;
using Scheduling.Application.Common;
using Scheduling.Domain.Appointments;

namespace Scheduling.Api.Controllers;

[Route("api/appointments")]
[Tags("Appointments")]
[Authorize]
public sealed class AppointmentsController : ApiController
{
    /// <summary>Books a slot. Use a startsAt returned by the availability endpoint.</summary>
    /// <response code="201">Booked.</response>
    /// <response code="400">Malformed request.</response>
    /// <response code="409">The slot was taken (possibly by a concurrent request) or you already have an appointment at that time.</response>
    /// <response code="422">A business rule was broken: outside working hours, too soon, off the 15-minute grid, time off...</response>
    [HttpPost]
    [Authorize(Policy = Policies.Customer)]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AppointmentDto>> Book(
        BookAppointmentCommand request, [FromServices] BookAppointmentHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(CurrentUserId, request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value)
            : Failure(result.Error);
    }

    /// <summary>The authenticated customer's appointments.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.Customer)]
    [ProducesResponseType<PagedList<AppointmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedList<AppointmentDto>>> Mine(
        [FromServices] ListMyAppointmentsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] AppointmentStatus? status = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null) =>
        OkOrProblem(await handler.HandleAsync(CurrentUserId, new AppointmentQuery(status, from, to), new PageRequest(page, pageSize), cancellationToken));

    /// <summary>All appointments (admin), filterable by professional, status and period.</summary>
    [HttpGet]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<PagedList<AppointmentDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedList<AppointmentDto>>> List(
        [FromServices] ListAppointmentsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] Guid? professionalId = null,
        [FromQuery] AppointmentStatus? status = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null) =>
        OkOrProblem(await handler.HandleAsync(new AppointmentQuery(status, from, to, professionalId), new PageRequest(page, pageSize), cancellationToken));

    /// <summary>Owner or admin. Someone else's appointment returns 404.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppointmentDto>> Get(Guid id, [FromServices] GetAppointmentHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, CurrentUserId, CurrentUserIsAdmin, cancellationToken));

    /// <summary>R9: the customer can cancel up to 2 hours before the start; an admin at any time.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AppointmentDto>> Cancel(
        Guid id, [FromServices] CancelAppointmentHandler handler, CancellationToken cancellationToken, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelAppointmentCommand? request = null) =>
        OkOrProblem(await handler.HandleAsync(id, CurrentUserId, CurrentUserIsAdmin, request ?? new CancelAppointmentCommand(null), cancellationToken));

    /// <summary>R10: only after the appointment has started.</summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AppointmentDto>> Complete(Guid id, [FromServices] CompleteAppointmentHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, cancellationToken));

    /// <summary>R10: only after the appointment has started.</summary>
    [HttpPost("{id:guid}/no-show")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<AppointmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AppointmentDto>> NoShow(Guid id, [FromServices] MarkNoShowHandler handler, CancellationToken cancellationToken) =>
        OkOrProblem(await handler.HandleAsync(id, cancellationToken));
}
