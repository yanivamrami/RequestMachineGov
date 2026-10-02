using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Requests.Domain.Entities;

namespace Requests.Application.Requests;

// [NEW] Keyset-pagination bookmark: "the last row the client saw". The next page is WHERE (sortKey, Id) is after it,
// which is an index seek at any depth (OFFSET would read and discard every skipped row) and stays stable when rows
// are added or removed between clicks.
// It stores all four sortable values of the last row, so every sort can use the same cursor shape (no per-type encoding).
// Hex-encoded JSON: opaque to clients and URL-safe without escaping (base64's + / = break in query strings).
// Not signed on purpose: a tampered cursor only moves the start position inside the caller's own permission-filtered rows.
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
