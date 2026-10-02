using Microsoft.EntityFrameworkCore;
using Requests.Application.Requests;
using Requests.Domain.Entities;
using Requests.Infrastructure.Persistence;

namespace Requests.Infrastructure.Repositories;

public sealed class RequestRepository : IRequestRepository
{
    private readonly RequestsDbContext _db;

    public RequestRepository(RequestsDbContext db)
    {
        _db = db;
    }

    // [OLD] Replaced: materializes the whole table (every column, every row, change-tracked)
    // before any filtering happens.
    // public Task<List<Request>> GetAllAsync(CancellationToken cancellationToken = default)
    // {
    //     return _db.Requests.ToListAsync(cancellationToken);
    // }

    // [NEW] Builds one query and executes it once (at ToListAsync):
    //  - Permission filter → WHERE OwnerId = @u OR AssignedToUserId = @u (admins skip it). Enforced server-side, inside the DB.
    //  - Stable order (newest first, Id as tie-breaker). The old query had no ORDER BY, so row order was undefined.
    //    It matches the (OwnerId|AssignedToUserId|CreatedAt, Id) indexes in RequestsDbContext.
    //  - Select → DTO: only the needed columns are read, and no change tracking (read-only).
    // ponytail: still unbounded — paging (keyset) arrives in Phase 2.
    public Task<List<RequestDto>> GetVisibleAsync(
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Request> query = _db.Requests;

        if (!isAdministrator)
            query = query.Where(x => x.OwnerId == currentUserId || x.AssignedToUserId == currentUserId);

        return query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new RequestDto(
                x.Id,
                x.RequestNumber,
                x.CustomerId,
                x.OwnerId,
                x.AssignedToUserId,
                x.Status,
                x.RequestType,
                x.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}
