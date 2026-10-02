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

    // [NEW] The permission filter and mapping moved into the repository query, so they run in the DB.
    // The service stays as the seam where Phase 2 adds cursor encode/decode.
    public async Task<IReadOnlyList<RequestDto>> GetRequestsAsync(
        int currentUserId,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
        => await _repository.GetVisibleAsync(currentUserId, isAdministrator, cancellationToken);
}
