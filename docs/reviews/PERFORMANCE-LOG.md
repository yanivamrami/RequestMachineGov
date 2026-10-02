# Performance Review Log

A record of every performance review, its findings, and how each finding was resolved. Append-only: newest run at the bottom. Full reports are in `performance-review-<scope>-<date>.md` next to this file.

**Status values:** Fixed · Deferred (planned, with target) · Accepted (won't fix, with reason) · Open

---

## Run 0 — Baseline analysis of the original code (2026-10-02)
**Scope:** original code on `main` @ `8be4ead`. **Method:** manual review during the assignment analysis, before any code changed.

| ID | Sev | Finding | Status | Fix |
|----|-----|---------|--------|-----|
| B-P1 | Sev 1 | `RequestRepository.GetAllAsync` loads the whole table (`ToListAsync`); the permission filter then runs in memory in `RequestService` | Fixed | Phase 1 `2e4669f`: `GetVisibleAsync` puts the permission `WHERE` + `.Select` to DTO inside one query |
| B-P2 | Sev 2 | Full entities materialized and change-tracked, then mapped in memory | Fixed | Phase 1 `2e4669f`: projection to `RequestDto` in the query (only the needed columns, no tracking) |
| B-P3 | Sev 2 | No `ORDER BY`, so result order is undefined | Fixed | Phase 1 `2e4669f`: `OrderByDescending(CreatedAt).ThenByDescending(Id)` |
| B-P4 | Sev 2 | No indexes declared | Fixed | Phase 1 `2e4669f`: 5 indexes in `RequestsDbContext.OnModelCreating` |
| B-P5 | Sev 1 | No pagination | Deferred → Phase 2 | Keyset paging (PLAN.md Phase 2) |

## Run 1 — Phase 1 delivery (2026-10-02)
**Scope:** `git diff main..fix/phase-1-current-code` @ `2e4669f`. **Skill:** `performance-reviewer`. **Report:** [performance-review-phase1-20261002.md](performance-review-phase1-20261002.md)

| ID | Sev | Confidence | Finding | Status | Fix / reason |
|----|-----|-----------|---------|--------|--------------|
| F-001 | Sev 1 | Confirmed | `GetVisibleAsync` has no row limit; an admin gets the entire table | Deferred → Phase 2 | Keyset `Take(pageSize + 1)`; marked with a `ponytail:` comment in code |
| F-002 | Sev 2 | Needs verification | `OwnerId = @u OR AssignedToUserId = @u` may fall back to a table scan | Accepted (for now) | Two composite indexes allow an index union. If `EXPLAIN` on a real DB shows a scan, rewrite as `Concat` (UNION ALL). Can't be measured on the in-memory provider |
| F-003 | Sev 3 | Likely | 5 indexes add write cost; `(Status, CreatedAt, Id)` serves a Phase 2 filter | Accepted | Read-heavy listing; drop indexes that turn out unused, per usage stats |
