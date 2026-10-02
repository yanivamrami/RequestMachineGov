namespace Requests.Application.Requests;

public sealed class RequestService : IRequestService
{
    private readonly IRequestRepository _repository;

    public RequestService(IRequestRepository repository)
    {
        _repository = repository;
    }

    // Turns the repository's PageSize + 1 rows into a page: the extra row only signals "there is more"
    // (no COUNT query), and the last row kept becomes the next cursor.
    public async Task<RequestPage> SearchAsync(
        RequestSearchQuery query,
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        RequestCursor? after = null;

        // The API already validated the cursor; this guard covers other callers, failing loudly rather than restarting at page 1.
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
