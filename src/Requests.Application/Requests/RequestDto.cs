using Requests.Domain.Entities;

namespace Requests.Application.Requests;

public sealed record RequestDto(
    int Id,
    string RequestNumber,
    int CustomerId,
    int OwnerId,
    int? AssignedToUserId,
    RequestStatus Status,
    RequestType RequestType,
    DateTime CreatedAt);

// One page of results. No total count: COUNT(*) over millions of filtered rows can cost more than the page itself.
// HasMore comes from fetching PageSize + 1 rows; NextCursor is null on the last page.
public sealed record RequestPage(
    IReadOnlyList<RequestDto> Items,
    string? NextCursor,
    bool HasMore);
