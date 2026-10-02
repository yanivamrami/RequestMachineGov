using System.ComponentModel.DataAnnotations;
using Requests.Domain.Entities;

namespace Requests.Application.Requests;

// Search criteria from the query string (GET /api/requests?status=New&status=InProgress&...).
// A GET because search is a read: safe to retry, cacheable, linkable; GET bodies aren't reliably supported.
// Data annotations + IValidatableObject let [ApiController] reject bad input with a per-field 400 before the action runs.
public sealed class RequestSearchQuery : IValidatableObject
{
    // Partial ("contains") match. Min 3 chars keeps the leading-wildcard LIKE selective; the character whitelist
    // matches the REQ-000123 format, so LIKE wildcards (% _) and other junk never reach the DB.
    [StringLength(20, MinimumLength = 3)]
    [RegularExpression("^[A-Za-z0-9-]+$", ErrorMessage = "Only letters, digits and '-' are allowed.")]
    public string? RequestNumber { get; set; }

    // Repeated param → array → SQL IN (...). Empty = any status.
    public RequestStatus[]? Status { get; set; }

    public RequestType? RequestType { get; set; }

    // Dates only (yyyy-MM-dd), UTC, both inclusive.
    public DateOnly? CreatedFrom { get; set; }
    public DateOnly? CreatedTo { get; set; }

    // Enums = the sort whitelist: any other value fails binding with a 400, so no arbitrary column can be sorted on.
    public RequestSortBy SortBy { get; set; } = RequestSortBy.CreatedAt;
    public SortDirection SortDir { get; set; } = SortDirection.Desc;

    // Bounded page size: the response size is capped no matter what the client asks for.
    [Range(1, 100)]
    public int PageSize { get; set; } = 25;

    // Opaque keyset bookmark from the previous page's nextCursor (see RequestCursor).
    [StringLength(1000)]
    public string? Cursor { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CreatedFrom > CreatedTo)
            yield return new("createdFrom must be on or before createdTo.", [nameof(CreatedFrom), nameof(CreatedTo)]);

        // Enum binding also accepts numbers ("status=99"), so undefined values are rejected explicitly.
        if (Status?.Any(s => !Enum.IsDefined(s)) == true)
            yield return new("Unknown status.", [nameof(Status)]);
        if (RequestType is { } type && !Enum.IsDefined(type))
            yield return new("Unknown request type.", [nameof(RequestType)]);
        if (!Enum.IsDefined(SortBy))
            yield return new("Unknown sort field.", [nameof(SortBy)]);
        if (!Enum.IsDefined(SortDir))
            yield return new("Unknown sort direction.", [nameof(SortDir)]);

        // A cursor is only valid for the sort it was created with; otherwise the "after this row" position is meaningless.
        if (Cursor is not null && (!RequestCursor.TryDecode(Cursor, out var cursor) || cursor.SortBy != SortBy || cursor.SortDir != SortDir))
            yield return new("Invalid cursor for this sort. Restart from the first page.", [nameof(Cursor)]);
    }
}

public enum RequestSortBy
{
    CreatedAt,
    RequestNumber,
    Status,
    RequestType
}

public enum SortDirection
{
    Asc,
    Desc
}
