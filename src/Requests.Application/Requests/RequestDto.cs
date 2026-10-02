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
