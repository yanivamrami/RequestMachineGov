using Microsoft.EntityFrameworkCore;
using Requests.Application.Requests;
using Requests.Domain.Entities;
using Requests.Infrastructure.Persistence;
using Requests.Infrastructure.Repositories;
using Xunit;

namespace Requests.Tests;

// [NEW] Tests run the real RequestRepository against EF in-memory (a fresh DB per test).
// The permission rule now lives in the repository's query, so a fake repository would no longer test it.
public class RequestServiceTests
{
    [Fact]
    public async Task Administrator_CanSeeAllRequests()
    {
        var service = CreateService(
            Create(1, ownerId: 1, assignedTo: 2),
            Create(2, ownerId: 3, assignedTo: 4));

        var result = await service.GetRequestsAsync(1, true);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task RegularUser_CanSeeOwnedOrAssignedRequests()
    {
        var service = CreateService(
            Create(1, ownerId: 1, assignedTo: 5),
            Create(2, ownerId: 3, assignedTo: 1),
            Create(3, ownerId: 3, assignedTo: 5),
            Create(4, ownerId: 3, assignedTo: null));

        var result = await service.GetRequestsAsync(1, false);

        // [NEW] Assert the exact set, not just the count, so a wrong-but-same-size result also fails.
        // Row 4 (unassigned, other owner) checks that the nullable AssignedToUserId doesn't leak rows.
        Assert.Equal([1, 2], result.Select(x => x.Id).Order());
    }

    // [NEW] The old query had no ORDER BY; results must now be newest first, with Id breaking ties.
    [Fact]
    public async Task Results_AreNewestFirst_WithIdAsTieBreaker()
    {
        var sameTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService(
            Create(1, ownerId: 1, assignedTo: null, createdAt: sameTime.AddDays(-1)),
            Create(2, ownerId: 1, assignedTo: null, createdAt: sameTime),
            Create(3, ownerId: 1, assignedTo: null, createdAt: sameTime));

        var result = await service.GetRequestsAsync(1, true);

        Assert.Equal([3, 2, 1], result.Select(x => x.Id));
    }

    // [OLD] Replaced: the fake repository returned a fixed list, so it never exercised the real query.
    // private sealed class FakeRequestRepository : IRequestRepository
    // {
    //     private readonly List<Request> _requests;
    //
    //     public FakeRequestRepository(List<Request> requests)
    //     {
    //         _requests = requests;
    //     }
    //
    //     public Task<List<Request>> GetAllAsync(CancellationToken cancellationToken = default)
    //         => Task.FromResult(_requests);
    // }
    // [NEW] Seeds a uniquely named in-memory DB, so tests are isolated from each other.
    private static RequestService CreateService(params Request[] requests)
    {
        var options = new DbContextOptionsBuilder<RequestsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new RequestsDbContext(options);
        db.Requests.AddRange(requests);
        db.SaveChanges();

        return new RequestService(new RequestRepository(db));
    }

    private static Request Create(int id, int ownerId, int? assignedTo, DateTime? createdAt = null)
        => new()
        {
            Id = id,
            RequestNumber = $"REQ-{id:000}",
            CustomerId = id,
            OwnerId = ownerId,
            AssignedToUserId = assignedTo,
            Status = RequestStatus.New,
            RequestType = RequestType.General,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
}
