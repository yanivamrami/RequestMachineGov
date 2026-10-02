# Backend Plan

Decisions: in-memory EF provider kept (designed and written as if it were SQL); keyset pagination; no total count; Request Number "contains" with a 3-character minimum.

## Delivery: 2 PRs

| Branch | Base | Contents |
|---|---|---|
| `main` | — | initial commit: the original code as received + `.gitignore`, `CLAUDE.md`, `PLAN.md`, `SESSION-LOG.md`, so each PR's diff shows only our changes |
| `fix/phase-1-current-code` | `main` | **PR 1** — Phase 1 |
| `feat/phase-2-search` | `fix/phase-1-current-code` (stacked) | **PR 2** — Phase 2. Rebase onto `main` after PR 1 merges |

Before each PR is opened: build + tests pass, then `performance-reviewer` and `security-review` run on that PR's changes.

Guiding rule: **the database returns one page, and nothing more.** Every filter, the permission rule, sorting and paging are composed into a single `IQueryable` that runs once.

---

## Phase 1 — Fix the existing code

No new features. The current `GET /api/requests` keeps its behavior, but correctly.

| # | Fix | Where | Why |
|---|-----|-------|-----|
| 1.1 | Add `CandidateTest.sln` containing all 5 projects | root | one `dotnet build` / `dotnet test` from the root; opens in an IDE |
| 1.2 | Move the permission filter into the query and project to `RequestDto` with `.Select` | `RequestRepository`, `IRequestRepository`, `RequestService` | today the whole table is loaded and then filtered in C# |
| 1.3 | Missing or invalid `X-User-Id` → **401**, instead of silently acting as user 1 | `RequestsController` | silent impersonation is a security bug |
| 1.4 | Register `RequestService` in `Program.cs`, not in Infrastructure | `DependencyInjection.cs`, `Program.cs` | Infrastructure shouldn't wire up Application services |
| 1.5 | Declare indexes with `HasIndex` (see the index list below) | `RequestsDbContext.OnModelCreating` | ignored by the in-memory provider, but they become a migration once a real DB replaces it |
| 1.6 | Global error handling, by environment (see below) | `Program.cs` | production never leaks exception details; development shows them for debugging |
| 1.7 | Rewrite tests against the real `RequestRepository` + EF in-memory (one DB per test); remove `FakeRequestRepository` | `tests/` | the fake would never exercise the real query, and that query is where the permission rule now lives |

### 1.6 Error handling, by environment

```csharp
builder.Services.AddProblemDetails();          // all error responses use the RFC 7807 shape
...
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();                 // Production: generic 500 { type, title, status, traceId }
// Development: ASP.NET Core enables the Developer Exception Page automatically —
// full exception + stack trace (returned as ProblemDetails JSON for API clients).
```

- **Production:** the response body contains no exception message, stack trace, SQL, or connection details. It includes only the `traceId`, which correlates with the server log. The exception itself is logged server-side by the exception handler middleware.
- **Development:** full details, to make debugging faster.
- **Safe default:** if `ASPNETCORE_ENVIRONMENT` is missing, ASP.NET Core falls back to Production (fail closed). Only `launchSettings.json` sets Development, so details never show unless explicitly enabled.
- **400 validation errors** are returned in every environment. They list only the invalid query fields the caller sent, nothing internal.
- **In the code:** this behavior is explained in a `[NEW]` comment in `Program.cs`, per the change-annotation rule.

Header-based identity stays (the spec provides it) and is marked `ponytail:` as a stand-in for JWT claims. Documented in the README.

---

## Phase 2 — Search & filtering

### API

```
GET /api/requests
  ?requestNumber=0012              contains, case-insensitive (input is uppercased), 3–20 chars
  &status=New&status=InProgress    repeatable → IN (...)
  &requestType=Legal               single value
  &createdFrom=2026-01-01          inclusive, UTC date
  &createdTo=2026-03-31            inclusive whole day → CreatedAt < to + 1 day
  &sortBy=createdAt                createdAt (default) | requestNumber | status | requestType
  &sortDir=desc                    asc | desc (default desc)
  &pageSize=25                     1–100, default 25
  &cursor=<opaque>                 from the previous response's nextCursor
```

Response:
```json
{ "items": [ RequestDto, ... ], "nextCursor": "eyJ...", "hasMore": true }
```
"Previous" is handled in the frontend by keeping a stack of the cursors it has already seen, so the backend has no backward keyset.

### Parts

1. **`RequestSearchQuery`** (Application) — a record bound with `[FromQuery]`.
   - Single-field rules use data annotations (`[StringLength(20, MinimumLength = 3)]`, `[Range(1,100)]`).
   - Cross-field rules (`createdFrom <= createdTo`, `sortBy` in the whitelist) use `IValidatableObject`.
   - `[ApiController]` then returns a 400 ProblemDetails automatically, and invalid enum values fail binding with a 400. No validation library is needed.
2. **Cursor** — base64 JSON of `{ sortBy, sortDir, value, id }`, where value/id are the last row's sort value and Id.
   - Decoded in the service.
   - A malformed cursor, or one whose `sortBy`/`sortDir` don't match the request → 400.
   - Opaque, so clients can't build their own.
3. **`RequestRepository.SearchAsync(query, userId, isAdmin, ct)`** composes, in order:
   1. permission — `OwnerId == u || AssignedToUserId == u` (skipped for admin)
   2. filters — `RequestNumber.Contains(term)`, `statuses.Contains(Status)`, type, date range
   3. keyset — e.g. desc: `Key < v || (Key == v && Id < id)`. One `switch` over the 4 whitelisted sort fields (enums compared as values, strings via `string.Compare`); every case translates to SQL.
   4. `OrderBy(key).ThenBy(Id)` in the same direction
   5. `Take(pageSize + 1)` → `.Select(→ RequestDto)` → `ToListAsync`
   6. the extra row only sets `hasMore`, then it's dropped
4. **Service** — decodes the cursor, calls the repository, and builds `nextCursor` from the last item.
5. **Controller** — reads the identity (from Phase 1), passes `[FromQuery] RequestSearchQuery`, returns the page.

### Indexes (declared in 1.5)

| Index | Serves |
|---|---|
| `RequestNumber` (unique) | lookups and uniqueness. "Contains" can't seek on it — see the note below |
| `(OwnerId, CreatedAt, Id)` | a regular user's default listing — the owner half of the permission OR |
| `(AssignedToUserId, CreatedAt, Id)` | the assigned half of the OR. With both indexes the DB can union the two |
| `(CreatedAt, Id)` | admin default listing, date range, keyset |
| `(Status, CreatedAt, Id)` | status filter + default sort |

Deliberately not added: single-column Status/Type indexes (only 4 values each, so poor selectivity), or an index per sort field (every index slows writes). Sorting by status/type/number relies on the permission and filter indexes having already narrowed the rows.

`ponytail:` "contains" (`LIKE '%x%'`) can't use a B-tree index. Within the permission-scoped rows it's acceptable. The upgrade path is a trigram index (Postgres `pg_trgm`) or a search engine.

### Tests (xUnit, real repository + EF in-memory)

1. A regular user sees only rows they own or are assigned to. An admin sees all. *(security)*
2. Paging through every page with keyset returns each matching row **exactly once**, including rows with tied sort values (same CreatedAt or same Status). *(correctness of the tie-breaker)*
3. Combined filters: multiple statuses + date range + "contains" return the expected set.
4. Validation: a 2-character requestNumber, `createdFrom > createdTo`, an unknown `sortBy`, and a tampered cursor are each rejected.

Optional: one `WebApplicationFactory` test for the 401 and 400 HTTP responses (`Program` is already `public partial`, but it needs the `Microsoft.AspNetCore.Mvc.Testing` package).

### Out of scope (README only)
Real DB + migrations, measured query plans, partitioning by CreatedAt, read replicas, a search engine, real authentication (JWT).
