# Performance Review — Phase 2 (`feat/phase-2-search` vs `fix/phase-1-current-code`)

**Stack detected:** .NET 8 / ASP.NET Core / EF Core (in-memory standing in for SQL) + Angular 21 (zoneless, signals)
**Checklists applied:** .NET / C#, Angular
**Files reviewed:** backend 9 (full: repository, service, query, cursor, controller, Program, tests), frontend 7 (sampled: search component + template, API service, interceptor)
**Findings:** 6 — 0 Critical / 3 High / 3 Low

### Executive Summary
Phase 1's Sev 1 (unbounded result) is resolved. Each request is one query that returns at most `PageSize + 1` rows, keyset pagination keeps every page the same cost at any depth, and there is no `COUNT(*)`. The remaining costs are query plans under specific sorts (status, type, or number for admins; any sort combined with the permission OR), which can only be confirmed with `EXPLAIN` on a real database. The keyset predicate shape was rewritten to be index-seekable during this review.

### Findings Overview

| ID | Sev | Confidence | Category | Location | Title |
|----|-----|-----------|----------|----------|-------|
| F-001 | Sev 2 | Likely | EF Core – Index usage | `RequestRepository.OrderBy/After` | Admin sort by Status/RequestType has no `(key, Id)` index, so each page is a scan + top-N sort |
| F-002 | Sev 2 | Needs verification | EF Core – Index usage | `RequestRepository` permission `Where` + ORDER BY | The OR permission filter can't return rows pre-ordered, so the user's whole row set is sorted per page |
| F-003 | Sev 2 | Likely | EF Core – Index usage | `RequestRepository.After` | Keyset written as `a < x OR (a = x AND id < y)`, which some optimizers won't seek. **Fixed** |
| F-004 | Sev 3 | Confirmed | EF Core – Index usage | `RequestRepository` `Contains` | `LIKE '%term%'` can't seek an index |
| F-005 | Sev 3 | Confirmed | CPU | `RequestSearchQuery.Validate` + `RequestService` | Cursor decoded twice per request |
| F-006 | Sev 3 | Likely | Angular – Change Detection | `request-search.component.html` | `err(...)` method calls in the template (~12 per check), no OnPush |

### Finding Details

#### F-001 · Sev 2 · Likely · EF Core – Index usage
**Description:** `ORDER BY Status, Id` (or `RequestType, Id`) with no permission filter (admin) matches no index: `(Status, CreatedAt, Id)` doesn't give `(Status, Id)` order. Each page becomes a full scan plus a top-N sort, about a second at 1M+ rows. Regular users are unaffected because their permission indexes narrow the rows first. `RequestNumber` sort is covered by its unique index.
**Fix (if admins sort this way in practice):**
```csharp
request.HasIndex(x => new { x.Status, x.Id });
request.HasIndex(x => new { x.RequestType, x.Id });
```
**Verification:** `EXPLAIN ANALYZE` of the admin `sortBy=status` query on a database with millions of rows.

#### F-002 · Sev 2 · Needs verification · EF Core – Index usage
**Description:** `OwnerId = @u OR AssignedToUserId = @u` combined with `ORDER BY CreatedAt DESC, Id DESC TOP 26` can use both indexes (index union), but the union isn't ordered, so all of the user's matching rows are sorted on every page. That's cheap for thousands of rows per user and costly for users with 100k+.
**Fix (if the plan shows it):** order and limit each half, then merge.
```csharp
var owned    = filtered.Where(x => x.OwnerId == u);
var assigned = filtered.Where(x => x.AssignedToUserId == u && x.OwnerId != u);
// each side: keyset + OrderBy + Take(n+1) (an index seek), then Concat + OrderBy + Take(n+1) over at most 2(n+1) rows
```
**Verification:** an actual execution plan for a user who owns or is assigned 100k+ rows.

#### F-003 · Sev 2 · Likely · EF Core – Index usage — **Fixed in this review**
**Description:** SQL Server often won't turn the OR-shaped keyset predicate into an index seek, scanning from the start of the index instead.
**Fix applied:** an equivalent form with a leading range: `key <= v AND (key < v OR Id < id)` (and the mirror for ascending). Equivalence is proven by `Paging_ReturnsEveryVisibleRowExactlyOnce_InSortOrder` across all 8 sort/direction combinations.

#### F-004 · Sev 3 · Confirmed · EF Core – Index usage
**Description:** A leading-wildcard `LIKE` scans the rows that remain after the other predicates. It's bounded by the 3-character minimum and the permission/filters, and marked with a `ponytail:` comment.
**Fix (at scale):** a trigram index (`CREATE INDEX ... USING gin (RequestNumber gin_trgm_ops)` on Postgres) or a search engine.

#### F-005 · Sev 3 · Confirmed · CPU
**Description:** The cursor is hex-decoded and JSON-parsed in validation, then again in the service. That's about 260 bytes, microseconds per request.
**Fix:** none needed. Keeping the service's own decode makes it safe for non-HTTP callers.

#### F-006 · Sev 3 · Likely · Angular – Change Detection
**Description:** `err('field')` runs about 12 times per change-detection pass. The app is zoneless and signal-driven, so passes happen only on events and signal changes, and each call is a few property reads.
**Fix (if the page grows):** `changeDetection: ChangeDetectionStrategy.OnPush` plus a `computed()` map of field errors.

### No issues found in
- Unbounded results: `Take(PageSize + 1)` with `[Range(1,100)]` on `PageSize`.
- No `COUNT`: `HasMore` comes from the extra row.
- Projection to `RequestDto`: no tracking, only the needed columns.
- Translatability: every predicate is a comparison, `Contains` or `IN`. No client-side evaluation.
- `CancellationToken` passed end to end; no sync-over-async.
- Frontend: `@for ... track r.id`, `switchMap` cancels superseded requests, page ≤ 100 rows (no virtualization needed), no subscription leaks (`takeUntilDestroyed`).

### Review Limitations
No real database, so F-001 and F-002 need execution plans. Frontend bundle size (213 kB) wasn't analyzed further.

---
## Review Metadata
- **Reviewed by:** Claude Performance Reviewer Skill
- **Checklists:** .NET / C#, Angular
- **Date:** 2026-10-02
- **Scope:** `git diff fix/phase-1-current-code..feat/phase-2-search` (src/, tests/, frontend/src/app)
