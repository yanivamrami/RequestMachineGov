using Microsoft.AspNetCore.Mvc;
using Requests.Application.Requests;

namespace Requests.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RequestsController : ControllerBase
{
    private readonly IRequestService _service;

    public RequestsController(IRequestService service)
    {
        _service = service;
    }

    // [OLD] Replaced: a missing or non-numeric X-User-Id silently became user 1, so an anonymous call
    // got user 1's data (impersonation by omission). Headers were read by hand, so Swagger couldn't show them.
    // // For the exercise, the current user is supplied through headers:
    // // X-User-Id: integer
    // // X-Is-Admin: true|false
    // [HttpGet]
    // public async Task<ActionResult<IReadOnlyList<RequestDto>>> Get(
    //     CancellationToken cancellationToken)
    // {
    //     var userId = ParseUserId(Request.Headers["X-User-Id"].FirstOrDefault());
    //     var isAdmin = string.Equals(
    //         Request.Headers["X-Is-Admin"].FirstOrDefault(),
    //         "true",
    //         StringComparison.OrdinalIgnoreCase);
    //
    //     var result = await _service.GetRequestsAsync(userId, isAdmin, cancellationToken);
    //     return Ok(result);
    // }
    //
    // private static int ParseUserId(string? value)
    //     => int.TryParse(value, out var userId) ? userId : 1;

    // [NEW] No identity → 401. A missing, non-numeric, or non-positive X-User-Id is rejected instead of defaulting to a real user.
    // [FromHeader] makes both headers visible and fillable in Swagger.
    // ponytail: identity comes from client-supplied headers (the exercise's stand-in). Anyone can send X-Is-Admin: true.
    // Production would read the user id and role from a validated JWT (User.Claims) via [Authorize].
    // [OLD] Replaced (Phase 2): no search criteria; returned the full visible list.
    // public async Task<ActionResult<IReadOnlyList<RequestDto>>> Get(
    //     [FromHeader(Name = "X-User-Id")] string? userIdHeader,
    //     [FromHeader(Name = "X-Is-Admin")] string? isAdminHeader,
    //     CancellationToken cancellationToken)
    //     ...
    //     var result = await _service.GetRequestsAsync(userId, isAdmin, cancellationToken);

    // [NEW] Search endpoint: GET /api/requests?requestNumber=&status=&requestType=&createdFrom=&createdTo=&sortBy=&sortDir=&pageSize=&cursor=
    // [FromQuery] is required: [ApiController] would otherwise infer [FromBody] for a complex type, and GET has no body.
    // Invalid criteria never reach this method: [ApiController] returns 400 ProblemDetails with per-field errors.
    // ponytail: that 400 check runs before the header check below, so an anonymous call with bad criteria gets 400, not 401.
    // With real auth ([Authorize]), authentication runs first and this goes away.
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RequestPage>> Get(
        [FromHeader(Name = "X-User-Id")] string? userIdHeader,
        [FromHeader(Name = "X-Is-Admin")] string? isAdminHeader,
        [FromQuery] RequestSearchQuery query,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(userIdHeader, out var userId) || userId <= 0)
            return Unauthorized();

        var isAdmin = string.Equals(isAdminHeader, "true", StringComparison.OrdinalIgnoreCase);

        var result = await _service.SearchAsync(query, userId, isAdmin, cancellationToken);
        return Ok(result);
    }
}
