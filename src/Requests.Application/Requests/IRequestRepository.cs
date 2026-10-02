using Requests.Domain.Entities;

namespace Requests.Application.Requests;

public interface IRequestRepository
{
    // [OLD] Replaced: returns every row in the table, and the caller filters by permission in memory.
    // At millions of rows each request would load the entire table into RAM.
    // Task<List<Request>> GetAllAsync(CancellationToken cancellationToken = default);

    // [NEW] Returns only the rows the caller may see, already projected to DTOs.
    // The permission rule becomes a WHERE clause in the DB query, so rows the user can't see never leave the database.
    Task<List<RequestDto>> GetVisibleAsync(
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}
