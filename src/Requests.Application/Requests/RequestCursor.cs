using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Requests.Domain.Entities;

namespace Requests.Application.Requests;

// Keyset-pagination bookmark: the last row the client saw. The next page starts after its (sort key, Id),
// which is an index seek at any depth (OFFSET reads and discards every skipped row) and stays stable when rows change.
// - Holds all four sortable values, so one cursor shape serves every sort.
// - Hex-encoded JSON: opaque and URL-safe without escaping (base64's + / = need escaping in query strings).
// - Not signed on purpose: tampering only moves the start position within the caller's own permitted rows.
public sealed record RequestCursor(
    RequestSortBy SortBy,
    SortDirection SortDir,
    int Id,
    DateTime CreatedAt,
    string RequestNumber,
    RequestStatus Status,
    RequestType RequestType)
{
    public static RequestCursor After(RequestDto last, RequestSortBy sortBy, SortDirection sortDir)
        => new(sortBy, sortDir, last.Id, last.CreatedAt, last.RequestNumber, last.Status, last.RequestType);

    public string Encode() => Convert.ToHexString(JsonSerializer.SerializeToUtf8Bytes(this));

    public static bool TryDecode(string value, [NotNullWhen(true)] out RequestCursor? cursor)
    {
        try
        {
            cursor = JsonSerializer.Deserialize<RequestCursor>(Convert.FromHexString(value));
            return cursor is { RequestNumber: not null };
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            cursor = null;
            return false;
        }
    }
}
