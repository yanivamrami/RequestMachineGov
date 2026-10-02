namespace Requests.Application.Requests;

public sealed class RequestService : IRequestService
{
    private readonly IRequestRepository _repository;

    public RequestService(IRequestRepository repository)
    {
        _repository = repository;
    }

    // [OLD] Replaced: loaded all requests, then applied the permission filter and DTO mapping in C# (in memory).
    // Correct output, but cost grows with the whole table instead of with the user's result.
    // public async Task<IReadOnlyList<RequestDto>> GetRequestsAsync(
    //     int currentUserId,
    //     bool isAdministrator,
    //     CancellationToken cancellationToken = default)
    // {
    //     var requests = await _repository.GetAllAsync(cancellationToken);
    //
    //     if (!isAdministrator)
    //     {
    //         requests = requests
    //             .Where(x => x.OwnerId == currentUserId || x.AssignedToUserId == currentUserId)
    //             .ToList();
    //     }
    //
    //     return requests.Select(x => new RequestDto(
    //         x.Id,
    //         x.RequestNumber,
    //         x.CustomerId,
    //         x.OwnerId,
    //         x.AssignedToUserId,
    //         x.Status,
    //         x.RequestType,
    //         x.CreatedAt)).ToList();
    // }

    // [OLD] Replaced (Phase 2): unpaged pass-through to the repository.
    // public async Task<IReadOnlyList<RequestDto>> GetRequestsAsync(
    //     int currentUserId,
    //     bool isAdministrator,
    //     CancellationToken cancellationToken = default)
    //     => await _repository.GetVisibleAsync(currentUserId, isAdministrator, cancellationToken);

    // [NEW] Turns the repository's PageSize + 1 rows into a page:
    // the extra row only means "there is more" (no COUNT query), and the last returned row becomes the next cursor.
    public async Task<RequestPage> SearchAsync(
        RequestSearchQuery query,
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        RequestCursor? after = null;

        // The API validates the cursor before this runs (RequestSearchQuery.Validate). This guard covers other callers:
        // a bad cursor fails loudly instead of silently restarting from page 1.
        if (query.Cursor is not null && !RequestCursor.TryDecode(query.Cursor, out after))
            throw new ArgumentException("Invalid cursor.", nameof(query));

        var rows = await _repository.SearchAsync(query, after, currentUserId, isAdministrator, cancellationToken);

        var hasMore = rows.Count > query.PageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var nextCursor = hasMore
            ? RequestCursor.After(rows[^1], query.SortBy, query.SortDir).Encode()
            : null;

        return new RequestPage(rows, nextCursor, hasMore);
    }
}
