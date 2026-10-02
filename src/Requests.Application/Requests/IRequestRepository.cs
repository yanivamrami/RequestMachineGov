using Requests.Domain.Entities;

namespace Requests.Application.Requests;

public interface IRequestRepository
{
    // [OLD] Replaced: returns every row in the table, and the caller filters by permission in memory.
    // At millions of rows each request would load the entire table into RAM.
    // Task<List<Request>> GetAllAsync(CancellationToken cancellationToken = default);

    // [OLD] Replaced (Phase 2): permission-scoped, but no filters and no row limit.
    // Task<List<RequestDto>> GetVisibleAsync(
    //     int currentUserId,
    //     bool isAdministrator,
    //     CancellationToken cancellationToken = default);

    // [NEW] One DB query: permission + filters + keyset position + order + limit. Returns up to PageSize + 1 rows;
    // the extra row tells the service whether another page exists, without a COUNT.
    Task<List<RequestDto>> SearchAsync(
        RequestSearchQuery query,
        RequestCursor? after,
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}
