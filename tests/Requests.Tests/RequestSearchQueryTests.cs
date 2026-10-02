using System.ComponentModel.DataAnnotations;
using Requests.Application.Requests;
using Requests.Domain.Entities;
using Xunit;

namespace Requests.Tests;

// Invalid input: each bad criterion is rejected on the right field.
// These are the same rules [ApiController] runs to produce the 400 ProblemDetails.
public class RequestSearchQueryTests
{
    private static readonly string ValidCursor = new RequestCursor(
        RequestSortBy.CreatedAt, SortDirection.Desc, 5, DateTime.UtcNow, "REQ-005", RequestStatus.New, RequestType.General).Encode();

    public static TheoryData<RequestSearchQuery, string> InvalidQueries => new()
    {
        { new() { RequestNumber = "RE" }, nameof(RequestSearchQuery.RequestNumber) },                     // < 3 chars
        { new() { RequestNumber = "REQ%" }, nameof(RequestSearchQuery.RequestNumber) },                   // LIKE wildcard
        { new() { PageSize = 0 }, nameof(RequestSearchQuery.PageSize) },
        { new() { PageSize = 101 }, nameof(RequestSearchQuery.PageSize) },
        { new() { CreatedFrom = new(2026, 2, 1), CreatedTo = new(2026, 1, 1) }, nameof(RequestSearchQuery.CreatedFrom) },
        { new() { Status = [(RequestStatus)99] }, nameof(RequestSearchQuery.Status) },                   // status=99 binds but is undefined
        { new() { SortBy = (RequestSortBy)9 }, nameof(RequestSearchQuery.SortBy) },
        { new() { Cursor = "not-hex" }, nameof(RequestSearchQuery.Cursor) },                              // tampered
        { new() { Cursor = ValidCursor, SortBy = RequestSortBy.Status }, nameof(RequestSearchQuery.Cursor) } // cursor from another sort
    };

    [Theory]
    [MemberData(nameof(InvalidQueries))]
    public void InvalidQuery_IsRejected_OnTheRightField(RequestSearchQuery query, string field)
    {
        var errors = Validate(query);

        Assert.Contains(errors, e => e.MemberNames.Contains(field));
    }

    [Fact]
    public void ValidQuery_WithMatchingCursor_IsAccepted()
    {
        var query = new RequestSearchQuery
        {
            RequestNumber = "req-0", Status = [RequestStatus.New], RequestType = RequestType.Legal,
            CreatedFrom = new(2026, 1, 1), CreatedTo = new(2026, 1, 1), PageSize = 100, Cursor = ValidCursor
        };

        Assert.Empty(Validate(query));
    }

    private static List<ValidationResult> Validate(RequestSearchQuery query)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(query, new ValidationContext(query), results, validateAllProperties: true);
        return results;
    }
}
