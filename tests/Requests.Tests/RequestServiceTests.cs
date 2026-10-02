using Microsoft.EntityFrameworkCore;
using Requests.Application.Requests;
using Requests.Domain.Entities;
using Requests.Infrastructure.Persistence;
using Requests.Infrastructure.Repositories;
using Xunit;

namespace Requests.Tests;

// Runs the real RequestRepository against EF in-memory (a fresh DB per test), because the permission rule
// lives in the repository's query and a fake repository wouldn't test it.
public class RequestServiceTests
{
    [Fact]
    public async Task Administrator_CanSeeAllRequests()
    {
        var service = CreateService(
            Create(1, ownerId: 1, assignedTo: 2),
            Create(2, ownerId: 3, assignedTo: 4));

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

        var result = (await service.SearchAsync(new RequestSearchQuery(), 1, false)).Items;

        // Exact set, not just the count, so a wrong-but-same-size result fails too.
        // Row 4 (unassigned, other owner) checks that a null AssignedToUserId doesn't leak rows.
        Assert.Equal([1, 2], result.Select(x => x.Id).Order());
    }

    // Default order: newest first, with Id breaking ties.
    [Fact]
    public async Task Results_AreNewestFirst_WithIdAsTieBreaker()
    {
        var sameTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService(
            Create(1, ownerId: 1, assignedTo: null, createdAt: sameTime.AddDays(-1)),
            Create(2, ownerId: 1, assignedTo: null, createdAt: sameTime),
            Create(3, ownerId: 1, assignedTo: null, createdAt: sameTime));

        var result = (await service.SearchAsync(new RequestSearchQuery(), 1, true)).Items;

        Assert.Equal([3, 2, 1], result.Select(x => x.Id));
    }

    // Keyset correctness: walking all pages returns each visible row exactly once, in order, for every sort and direction.
    // The data has many ties (3 timestamps, 4 statuses, 4 types), so this fails if Id stops breaking ties or the
    // keyset predicate and ORDER BY disagree. Permission scoping is checked on every page too.
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

        // PageSize 3 on purpose: with 4 rows per status/type, page boundaries fall inside groups of tied values,
        // so the Id tie-breaker is exercised (page size 4 hid a broken tie-breaker).
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

    // Filters combine with AND: multi-status (IN), type, inclusive date range and case-insensitive "contains".
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

    // The largest valid end date must mean "no upper bound" (it once overflowed into a 500),
    // while an ordinary end date stays inclusive.
    [Fact]
    public async Task CreatedTo_MaxDate_ReturnsResultsInsteadOfOverflowing()
    {
        var jan = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService(Create(1, 1, null, jan), Create(2, 1, null, jan.AddDays(5)));

        var max = await service.SearchAsync(new RequestSearchQuery { CreatedTo = DateOnly.MaxValue }, 1, true);
        var ordinary = await service.SearchAsync(new RequestSearchQuery { CreatedTo = new DateOnly(2026, 1, 1) }, 1, true);

        Assert.Equal([1, 2], max.Items.Select(x => x.Id).Order());
        Assert.Equal([1], ordinary.Items.Select(x => x.Id));
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

    // A uniquely named in-memory DB per test keeps tests isolated.
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
