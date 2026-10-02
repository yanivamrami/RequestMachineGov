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

    // For the exercise, the current user is supplied through headers:
    // X-User-Id: integer
    // X-Is-Admin: true|false
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RequestDto>>> Get(
        CancellationToken cancellationToken)
    {
        var userId = ParseUserId(Request.Headers["X-User-Id"].FirstOrDefault());
        var isAdmin = string.Equals(
            Request.Headers["X-Is-Admin"].FirstOrDefault(),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var result = await _service.GetRequestsAsync(userId, isAdmin, cancellationToken);
        return Ok(result);
    }

    private static int ParseUserId(string? value)
        => int.TryParse(value, out var userId) ? userId : 1;
}
