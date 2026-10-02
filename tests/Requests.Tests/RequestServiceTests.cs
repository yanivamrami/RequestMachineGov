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

        // [OLD] Replaced (Phase 2): var result = await service.GetRequestsAsync(1, true);
        // [NEW] Same scenario through the paged search API.
        var result = (await service.SearchAsync(new RequestSearchQuery(), 1, true)).Items;

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

        // [OLD] Replaced (Phase 2): var result = await service.GetRequestsAsync(1, false);
        // [NEW] Same scenario through the paged search API.
        var result = (await service.SearchAsync(new RequestSearchQuery(), 1, false)).Items;

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

        // [OLD] Replaced (Phase 2): var result = await service.GetRequestsAsync(1, true);
        // [NEW] Same scenario through the paged search API (default sort = createdAt desc).
        var result = (await service.SearchAsync(new RequestSearchQuery(), 1, true)).Items;

        Assert.Equal([3, 2, 1], result.Select(x => x.Id));
    }

    // [NEW] Keyset correctness: walking every page returns each visible row exactly once, in the right order, for every
    // sort field and direction. The data has many ties (3 timestamps, 4 statuses, 4 types), so this fails if Id stops
    // breaking ties or the keyset predicate and ORDER BY disagree. It also checks permission scoping on every page.
    [Theory]
    [InlineData(RequestSortBy.CreatedAt, SortDirection.Desc)]
    [InlineData(RequestSortBy.CreatedAt, SortDirection.Asc)]
    [InlineData(RequestSortBy.RequestNumber, SortDirection.Desc)]
    [InlineData(RequestSortBy.RequestNumber, SortDirection.Asc)]
    [InlineData(RequestSortBy.Status, SortDirection.Desc)]
    [InlineData(RequestSortBy.Status, SortDirection.Asc)]
    [InlineData(RequestSortBy.RequestType, SortDirection.Desc)]
    [InlineData(RequestSortBy.RequestType, SortDirection.Asc)]
    public async Task Paging_ReturnsEveryVisibleRowExactlyOnce_InSortOrder(RequestSortBy sortBy, SortDirection sortDir)
    {
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(1, 23)
            .Select(i => Create(i, ownerId: i % 3 == 0 ? 2 : 1, assignedTo: null,
                createdAt: day.AddHours(i % 3),
                status: (RequestStatus)(i % 4 + 1),
                type: (RequestType)(i * 7 % 4 + 1)))
            .ToArray();
        var service = CreateService(rows);

        var visible = rows.Where(x => x.OwnerId == 1).ToList();
        var expected = Expected(visible, sortBy, sortDir).Select(x => x.Id).ToList();

        // PageSize 3 on purpose: 16 visible rows with 4 per status/type, so page boundaries fall inside groups of tied
        // values and the Id tie-breaker is actually exercised (a page size of 4 hid a broken tie-breaker, verified).
        var seen = new List<int>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await service.SearchAsync(
                new RequestSearchQuery { SortBy = sortBy, SortDir = sortDir, PageSize = 3, Cursor = cursor }, 1, false);
            seen.AddRange(page.Items.Select(x => x.Id));
            Assert.Equal(page.HasMore, page.NextCursor is not null);
            cursor = page.NextCursor;
            Assert.True(++pages <= 10, "paging did not terminate");
        } while (cursor is not null);

        Assert.Equal(expected, seen);
    }

    // [NEW] Filters combine with AND: multi-status (IN), type, inclusive date range, and case-insensitive "contains".
    [Fact]
    public async Task Filters_CombineStatusTypeDateRangeAndNumber()
    {
        var jan = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService(
            Create(1, 1, null, jan.AddDays(0).AddHours(23), RequestStatus.New, RequestType.Legal),        // match (first day, late)
            Create(2, 1, null, jan.AddDays(2).AddHours(23), RequestStatus.InProgress, RequestType.Legal), // match (last day, inclusive "to")
            Create(3, 1, null, jan.AddDays(3), RequestStatus.New, RequestType.Legal),                     // after "to"
            Create(4, 1, null, jan.AddDays(1), RequestStatus.Completed, RequestType.Legal),               // status not selected
            Create(5, 1, null, jan.AddDays(1), RequestStatus.New, RequestType.Payment),                   // other type
            Create(6, 1, null, jan.AddDays(-1), RequestStatus.New, RequestType.Legal),                    // before "from"
            Create(10, 1, null, jan.AddDays(1), RequestStatus.New, RequestType.Legal));                   // "REQ-010" doesn't contain "Q-00"

        var page = await service.SearchAsync(new RequestSearchQuery
        {
            RequestNumber = "q-00", // lower-case on purpose: the search is case-insensitive
            Status = [RequestStatus.New, RequestStatus.InProgress],
            RequestType = RequestType.Legal,
            CreatedFrom = new DateOnly(2026, 1, 1),
            CreatedTo = new DateOnly(2026, 1, 3)
        }, 1, true);

        Assert.Equal([1, 2], page.Items.Select(x => x.Id).Order());
    }

    private static IEnumerable<Request> Expected(List<Request> rows, RequestSortBy sortBy, SortDirection sortDir)
    {
        Func<Request, object> key = sortBy switch
        {
            RequestSortBy.CreatedAt => x => x.CreatedAt,
            RequestSortBy.RequestNumber => x => x.RequestNumber,
            RequestSortBy.Status => x => x.Status,
            _ => x => x.RequestType
        };
        return sortDir == SortDirection.Desc
            ? rows.OrderByDescending(key).ThenByDescending(x => x.Id)
            : rows.OrderBy(key).ThenBy(x => x.Id);
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

    // [OLD] Replaced (Phase 2): private static Request Create(int id, int ownerId, int? assignedTo, DateTime? createdAt = null)
    // [NEW] Status and type are configurable so the paging and filter tests can build ties and mixed data.
    private static Request Create(int id, int ownerId, int? assignedTo, DateTime? createdAt = null,
        RequestStatus status = RequestStatus.New, RequestType type = RequestType.General)
        => new()
        {
            Id = id,
            RequestNumber = $"REQ-{id:000}",
            CustomerId = id,
            OwnerId = ownerId,
            AssignedToUserId = assignedTo,
            Status = status,
            RequestType = type,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
}
