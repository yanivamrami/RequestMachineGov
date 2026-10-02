namespace Requests.Application.Requests;

public interface IRequestService
{
    // [OLD] Replaced: returned every visible request in one list. No filters, no paging.
    // Task<IReadOnlyList<RequestDto>> GetRequestsAsync(
    //     int currentUserId,
    //     bool isAdministrator,
    //     CancellationToken cancellationToken = default);

    // [NEW] Filtered, sorted, keyset-paged search, scoped to what the caller may see.
    Task<RequestPage> SearchAsync(
        RequestSearchQuery query,
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}
