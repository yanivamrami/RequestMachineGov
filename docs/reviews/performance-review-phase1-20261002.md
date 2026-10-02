# Performance Review — Phase 1 (`fix/phase-1-current-code` vs `main`)

**Stack detected:** .NET 8 / ASP.NET Core Web API / EF Core (in-memory provider standing in for SQL)
**Checklists applied:** .NET / C# (EF Core queries, tracking, async, payload)
**Files reviewed:** 8 changed files (full), plus unchanged `DbSeeder.cs` and `Request.cs` for context
**Findings:** 3 — 1 Critical (deferred by plan) / 1 High / 1 Low

### Executive Summary
Phase 1 removes the main scaling bug: the full-table `ToListAsync` followed by in-memory filtering is now a single translatable query (permission `WHERE`, `ORDER BY`, projection to DTO), and matching composite indexes are declared. The remaining big cost is that results are still unbounded. This is deliberately deferred to Phase 2 (keyset paging), but it blocks production use. The permission `OR` across two columns is the one query shape whose index usage must be confirmed on a real database.

### Findings Overview

| ID | Sev | Confidence | Category | Location | Title |
|----|-----|-----------|----------|----------|-------|
| F-001 | Sev 1 | Confirmed | EF Core – Unbounded query | `RequestRepository.cs` `GetVisibleAsync` | No `Take`; an admin call returns the entire table |
| F-002 | Sev 2 | Needs verification | EF Core – Index usage | `RequestRepository.cs` permission `Where` | `OwnerId = @u OR AssignedToUserId = @u` may fall back to a scan |
| F-003 | Sev 3 | Likely | EF Core – Index write cost | `RequestsDbContext.cs` `OnModelCreating` | 5 indexes, one serving a not-yet-built filter |

### Finding Details

#### F-001 · Sev 1 · Confirmed · EF Core – Unbounded query
**Location:** `RequestRepository.GetVisibleAsync`
**Description:** The query is filtered and projected, but has no row limit. For an admin it returns every row (millions); for a user it returns all their rows. The cost is per request and grows linearly with the table.
**Fix (Phase 2, planned):** keyset paging. `Where(CreatedAt < @c || (CreatedAt == @c && Id < @id)).OrderByDescending(CreatedAt).ThenByDescending(Id).Take(pageSize + 1)`.
**Status:** Deferred by design to PR 2; noted with a `ponytail:` comment in code.

#### F-002 · Sev 2 · Needs verification · EF Core – Index usage
**Location:** `RequestRepository.GetVisibleAsync`, `Where(x => x.OwnerId == u || x.AssignedToUserId == u)`
**Description:** The two composite indexes allow an index-union / bitmap-OR plan, but optimizers sometimes pick a scan for OR predicates, especially combined with ORDER BY + TOP.
**Fix (only if the plan shows a scan):** split into two seeks and merge.
```csharp
var owned    = _db.Requests.Where(x => x.OwnerId == u);
var assigned = _db.Requests.Where(x => x.AssignedToUserId == u && x.OwnerId != u);
var query    = owned.Concat(assigned); // UNION ALL, each side seeks its own index
```
**Verification:** `EXPLAIN ANALYZE` (Postgres) or the actual execution plan (SQL Server) on a database seeded with millions of rows.

#### F-003 · Sev 3 · Likely · EF Core – Index write cost
**Location:** `RequestsDbContext.OnModelCreating`
**Description:** Each insert or update of Status/CreatedAt maintains 5 indexes plus the PK. `(Status, CreatedAt, Id)` serves a filter that only arrives in Phase 2. The cost is acceptable for a read-heavy listing.
**Fix:** keep it; drop any index that query plans show as unused after Phase 2 (`sys.dm_db_index_usage_stats` / `pg_stat_user_indexes`).

### No issues found in
- Tracking: projection to `RequestDto` means no change tracking, so `AsNoTracking` isn't needed.
- Client-side evaluation: all predicates translate (plain comparisons, no custom methods).
- `CancellationToken` passed controller → service → `ToListAsync`.
- Async: no `.Result`/`.Wait()`, no sync I/O on the request path.
- DI lifetimes: `DbContext`, repository and service are all scoped; no captive dependency.
- Error-handling middleware: `UseExceptionHandler` only on the error path; `CustomizeProblemDetails` is a dictionary insert per error response.

### Review Limitations
The in-memory provider has no SQL, indexes or query plans, so index usage (F-002) and actual latency can't be established statically or measured here.

---
## Review Metadata
- **Reviewed by:** Claude Performance Reviewer Skill
- **Checklists:** .NET / C#
- **Date:** 2026-10-02
- **Scope:** `git diff main..fix/phase-1-current-code` (src/, tests/)
