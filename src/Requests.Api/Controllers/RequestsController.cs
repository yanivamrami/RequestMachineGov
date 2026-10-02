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

    // GET /api/requests: filtered, sorted, keyset-paged search.
    // [FromQuery] is required because [ApiController] would otherwise bind a complex type from the body, and GET has none.
    // Invalid criteria never get here: [ApiController] answers 400 ProblemDetails with per-field errors.
    // ponytail: identity comes from client-supplied headers (the exercise's stand-in), so anyone can claim admin.
    // Real auth would read the user and role from a validated JWT via [Authorize]; that also makes 401 come before 400.
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
        // Never fall back to a default user: no valid identity means 401.
        if (!int.TryParse(userIdHeader, out var userId) || userId <= 0)
            return Unauthorized();

        var isAdmin = string.Equals(isAdminHeader, "true", StringComparison.OrdinalIgnoreCase);

        var result = await _service.SearchAsync(query, userId, isAdmin, cancellationToken);
        return Ok(result);
    }
}
