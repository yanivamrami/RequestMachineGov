namespace Requests.Application.Requests;

public interface IRequestService
{
    // Filtered, sorted, keyset-paged search, limited to what the caller may see.
    Task<RequestPage> SearchAsync(
        RequestSearchQuery query,
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}
