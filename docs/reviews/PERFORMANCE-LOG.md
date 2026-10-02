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

## Run 2 — Phase 2 delivery (2026-10-02)
**Scope:** `git diff fix/phase-1-current-code..feat/phase-2-search` (backend + frontend). **Skill:** `performance-reviewer`. **Report:** [performance-review-phase2-20261002.md](performance-review-phase2-20261002.md)

**Carried from Run 1:** F-001 (unbounded result) → **Fixed** by keyset `Take(PageSize + 1)`. B-P5 (no pagination) → **Fixed**.

| ID | Sev | Confidence | Finding | Status | Fix / reason |
|----|-----|-----------|---------|--------|--------------|
| F-001 | Sev 2 | Likely | Admin sort by Status/RequestType has no `(key, Id)` index, so each page is a scan + top-N sort | Accepted | Regular users are narrowed by permission indexes first. Add `(Status, Id)` / `(RequestType, Id)` if admin usage shows it |
| F-002 | Sev 2 | Needs verification | The permission OR can't return rows pre-ordered, so the user's rows are sorted per page | Accepted | Cheap for typical users. Upgrade: per-side keyset + `Concat` merge (Run 1 F-002, extended) |
| F-003 | Sev 2 | Likely | OR-shaped keyset predicate may not seek | **Fixed** | Rewritten as `key <= v AND (key < v OR Id < id)`; the paging test (8 sort combinations) proves equivalence |
| F-004 | Sev 3 | Confirmed | `LIKE '%term%'` can't seek | Accepted | 3-character minimum + scoped rows; `ponytail:` comment; trigram index at scale |
| F-005 | Sev 3 | Confirmed | Cursor decoded twice per request | Accepted | Microseconds; the service decode protects non-HTTP callers |
| F-006 | Sev 3 | Likely | Template method calls, no OnPush | Accepted | Zoneless + signals, ~12 cheap calls per pass; OnPush + `computed` if the page grows |

**Also found during Phase 2 test writing:** the keyset paging test used page size 4 with exactly 4 rows per status/type, so it never exercised the Id tie-breaker. Verified by deliberately breaking the tie-breaker (the test still passed). Fixed with page size 3; the same mutation now fails the test.

## Run 3 — Phase 3 delivery: UI redesign (2026-10-02)
**Scope:** `git diff feat/phase-2-search..feat/phase-3-ui-redesign -- frontend` (PR #3). **Skill:** `performance-reviewer` (Angular checklist). **Report:** [performance-review-phase3-20261002.md](performance-review-phase3-20261002.md)

| ID | Sev | Confidence | Finding | Status | Fix / reason |
|----|-----|-----------|---------|--------|--------------|
| F-001 | Sev 3 | Confirmed | Google Fonts stylesheet is render-blocking and needs 2 extra connections | Open, user decision | Self-host the woff2 with `@font-face` (also resolves security F-001) |
| F-002 | Sev 3 | Likely | `ariaSort()` ×3 per header and `label()` per row in the template | Accepted | Zoneless + ≤ 100 rows, trivial functions; `@let` / pure pipe if the page grows |

## Run 4 — Independent code review of PRs 1–3 by GPT-6 Astra (2026-10-02)
**Scope:** PRs #1–#3 (combined code). **Reviewer:** GPT-6 Astra (a different AI model). **Report:** [code-review-pr1-pr2-pr3-20261002.md](code-review-pr1-pr2-pr3-20261002.md). Security items are in SECURITY-LOG Run 4. **Fixed in:** PR #6.

| ID | Sev | Finding | Status | Fix / reason |
|----|-----|---------|--------|--------------|
| F3 | P2 | Sorting by `(Status, Id)` / `(RequestType, Id)` has no matching index, so admin pages are a scan + sort on a real DB | Accepted (same as Run 2 F-001) | Regular users are narrowed by permission indexes first. Add `(Status, Id)` / `(RequestType, Id)` if admin usage shows it. **README fixed:** it claimed "every page costs the same at any depth" without qualification; now it says that holds only where an index matches the sort |
| F5 | P2 (accessibility) | Input borders 1.57:1 against the surface; WCAG 1.4.11 needs 3:1 | Fixed | New `--control-border: oklch(62% .014 70)` for inputs/selects/ghost buttons: 3.6:1 (3.4:1 on `--surface-2`), computed via OKLCH → sRGB luminance |
| F6 | P2 (layout) | Pager clipped at 320px (298px row in a 288px card) | Fixed | `.pager-nav` wraps; tighter spacing below 30rem. Verified at 320px: nav 31–289px inside card 16–304px |
| Note | Low | Component CSS over its 4 kB warning budget | Accepted | Now 4.64 kB after the F5/F6 rules. A warning, not a runtime cost |
| Note | — | Scalability is a design intention on EF InMemory, not a measured result; `LIKE '%term%'` can scan for admins | Accepted (known, Run 1 F-002 / Run 2 F-004) | README "unfinished" #1: Postgres + `EXPLAIN ANALYZE` + `pg_trgm` |

## Run 5 — Phase 6 delivery: review fixes (2026-10-02)
**Scope:** PR #6 code diff. **Skill:** `performance-reviewer` criteria; small diff, read in full. **Report:** this entry.

**Findings:** none new.
- The `MaxValue` check removes a predicate; it never adds one.
- The identity clear makes no extra HTTP calls, because `switchMap` still cancels the previous request.
- `UseStatusCodePages` only acts on empty error responses.
- CSS budget: see Run 4 note.
