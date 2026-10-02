using Requests.Domain.Entities;

namespace Requests.Application.Requests;

public interface IRequestRepository
{
    // One DB query: permission + filters + keyset position + order + limit.
    // Returns up to PageSize + 1 rows; the extra row tells the service whether another page exists, without a COUNT.
    Task<List<RequestDto>> SearchAsync(
        RequestSearchQuery query,
        RequestCursor? after,
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);
}
