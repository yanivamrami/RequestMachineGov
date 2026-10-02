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

    // [OLD] Replaced (Phase 2): permission filter, order and projection were right, but there were no filters
    // and no row limit, so an admin still received the entire table.
    // public Task<List<RequestDto>> GetVisibleAsync(
    //     int currentUserId,
    //     bool isAdministrator,
    //     CancellationToken cancellationToken = default)
    // {
    //     IQueryable<Request> query = _db.Requests;
    //
    //     if (!isAdministrator)
    //         query = query.Where(x => x.OwnerId == currentUserId || x.AssignedToUserId == currentUserId);
    //
    //     return query
    //         .OrderByDescending(x => x.CreatedAt)
    //         .ThenByDescending(x => x.Id)
    //         .Select(x => new RequestDto(
    //             x.Id,
    //             x.RequestNumber,
    //             x.CustomerId,
    //             x.OwnerId,
    //             x.AssignedToUserId,
    //             x.Status,
    //             x.RequestType,
    //             x.CreatedAt))
    //         .ToListAsync(cancellationToken);
    // }

    // [NEW] Composes one IQueryable and executes it once (at ToListAsync), so the DB returns a single page:
    //   permission → filters → keyset "after cursor" → ORDER BY (sort key, Id) → TOP (PageSize + 1) → only DTO columns.
    // Every step is a plain comparison / Contains / IN, all of which translate to SQL. Nothing is evaluated in C#.
    public Task<List<RequestDto>> SearchAsync(
        RequestSearchQuery q,
        RequestCursor? after,
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Request> query = _db.Requests;

        // Permission first, enforced server-side in the WHERE clause: rows the user can't see never leave the DB.
        if (!isAdministrator)
            query = query.Where(x => x.OwnerId == currentUserId || x.AssignedToUserId == currentUserId);

        // Partial match → LIKE '%term%'. Uppercased because numbers are stored as "REQ-..." and the in-memory provider
        // compares case-sensitively (SQL collations usually don't).
        // ponytail: a leading-wildcard LIKE can't seek an index; it scans the rows already narrowed by permission/filters.
        // At larger scale: a trigram index (Postgres pg_trgm) or a search engine.
        if (!string.IsNullOrEmpty(q.RequestNumber))
        {
            var term = q.RequestNumber.ToUpperInvariant();
            query = query.Where(x => x.RequestNumber.Contains(term));
        }

        if (q.Status is { Length: > 0 } statuses)
            query = query.Where(x => statuses.Contains(x.Status)); // IN (...)

        if (q.RequestType is { } type)
            query = query.Where(x => x.RequestType == type);

        // Inclusive date range on a DateTime column: >= start of "from" day, < start of the day after "to".
        if (q.CreatedFrom is { } from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAt >= fromUtc);
        }

        if (q.CreatedTo is { } to)
        {
            var toExclusiveUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(x => x.CreatedAt < toExclusiveUtc);
        }

        var descending = q.SortDir == SortDirection.Desc;

        if (after is not null)
            query = After(query, q.SortBy, descending, after);

        return OrderBy(query, q.SortBy, descending)
            .Take(q.PageSize + 1)
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

    // [NEW] Keyset predicate: rows strictly after the cursor in (sort key, Id) order. Id breaks ties, so rows sharing a
    // sort value (same status, same timestamp) are neither repeated nor skipped across pages.
    // Written as "key <= v AND (key < v OR Id < id)" rather than the equivalent "key < v OR (key = v AND Id < id)":
    // the leading range on the key lets the DB seek the (key, ..., Id) index instead of scanning (perf review F-003).
    // An explicit switch rather than expression-tree tricks: each line is readable and translates to SQL as written.
    private static IQueryable<Request> After(IQueryable<Request> q, RequestSortBy sortBy, bool descending, RequestCursor c)
        => (sortBy, descending) switch
        {
            (RequestSortBy.CreatedAt, true) => q.Where(x => x.CreatedAt <= c.CreatedAt && (x.CreatedAt < c.CreatedAt || x.Id < c.Id)),
            (RequestSortBy.CreatedAt, false) => q.Where(x => x.CreatedAt >= c.CreatedAt && (x.CreatedAt > c.CreatedAt || x.Id > c.Id)),
            (RequestSortBy.RequestNumber, true) => q.Where(x => string.Compare(x.RequestNumber, c.RequestNumber) <= 0 && (string.Compare(x.RequestNumber, c.RequestNumber) < 0 || x.Id < c.Id)),
            (RequestSortBy.RequestNumber, false) => q.Where(x => string.Compare(x.RequestNumber, c.RequestNumber) >= 0 && (string.Compare(x.RequestNumber, c.RequestNumber) > 0 || x.Id > c.Id)),
            (RequestSortBy.Status, true) => q.Where(x => x.Status <= c.Status && (x.Status < c.Status || x.Id < c.Id)),
            (RequestSortBy.Status, false) => q.Where(x => x.Status >= c.Status && (x.Status > c.Status || x.Id > c.Id)),
            (RequestSortBy.RequestType, true) => q.Where(x => x.RequestType <= c.RequestType && (x.RequestType < c.RequestType || x.Id < c.Id)),
            (RequestSortBy.RequestType, false) => q.Where(x => x.RequestType >= c.RequestType && (x.RequestType > c.RequestType || x.Id > c.Id)),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy))
        };

    // [NEW] ORDER BY must match the keyset predicate exactly (same key, same direction, Id last), or pages overlap.
    private static IQueryable<Request> OrderBy(IQueryable<Request> q, RequestSortBy sortBy, bool descending)
        => (sortBy, descending) switch
        {
            (RequestSortBy.CreatedAt, true) => q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id),
            (RequestSortBy.CreatedAt, false) => q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            (RequestSortBy.RequestNumber, true) => q.OrderByDescending(x => x.RequestNumber).ThenByDescending(x => x.Id),
            (RequestSortBy.RequestNumber, false) => q.OrderBy(x => x.RequestNumber).ThenBy(x => x.Id),
            (RequestSortBy.Status, true) => q.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            (RequestSortBy.Status, false) => q.OrderBy(x => x.Status).ThenBy(x => x.Id),
            (RequestSortBy.RequestType, true) => q.OrderByDescending(x => x.RequestType).ThenByDescending(x => x.Id),
            (RequestSortBy.RequestType, false) => q.OrderBy(x => x.RequestType).ThenBy(x => x.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(sortBy))
        };
}
