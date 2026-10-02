# Security Review Log

A record of every security review, its findings, and how each finding was resolved. Append-only: newest run at the bottom. Full reports are in `security-review-<scope>-<date>.md` next to this file.

**Status values:** Fixed · Deferred (planned, with target) · Accepted (won't fix, with reason) · Open

---

## Run 0 — Baseline analysis of the original code (2026-10-02)
**Scope:** original code on `main` @ `8be4ead`. **Method:** manual review during the assignment analysis, before any code changed.

| ID | Severity | Finding | Status | Fix |
|----|----------|---------|--------|-----|
| B-S1 | High | A missing or non-numeric `X-User-Id` silently became user 1, so an anonymous caller got user 1's data | Fixed | Phase 1 `2e4669f`: missing, invalid or non-positive id → 401 (`RequestsController`) |
| B-S2 | High | `X-Is-Admin` is client-controlled, so any caller can claim admin and see all requests | Accepted (exercise constraint) | The spec provides header identity as a stand-in. Documented with a `ponytail:` comment in code and to go in the README; the real fix is a validated JWT (`[Authorize]`, `User.Claims`) |
| B-S3 | Medium | No global error handling, so production behavior for unhandled exceptions was undefined | Fixed | Phase 1 `2e4669f`: `AddProblemDetails` + `UseExceptionHandler` outside Development |

## Run 1 — Phase 1 delivery (2026-10-02)
**Scope:** `git diff main..fix/phase-1-current-code` @ `2e4669f`. **Skill:** `security-review` (sub-agent analysis, confidence threshold ≥ 8). **Report:** [security-review-phase1-20261002.md](security-review-phase1-20261002.md)

**Pre-review verification:** a temporary throwing endpoint (removed afterwards) was run in Production and Development.

| ID | Severity | Finding | Status | Fix |
|----|----------|---------|--------|-----|
| V-001 | Low | The production 500 ProblemDetails had no `traceId`, so a client error couldn't be matched to the server log entry | Fixed | Phase 1 `2e4669f`: `CustomizeProblemDetails` adds `traceId` to every error response. Re-verified: still no exception message or stack trace in Production |

**Skill findings:** none at or above the reporting bar. Checked: the identity 401 path (including duplicate headers), the permission predicate (including NULL `AssignedToUserId`), parameterized LINQ (no injection), DTO field exposure, error-detail exposure by environment, Swagger Development-only.
**Carried forward:** B-S2 (accepted).

## Run 2 — Phase 2 delivery (2026-10-02)
**Scope:** `git diff fix/phase-1-current-code..feat/phase-2-search` (backend + frontend). **Skill:** `security-review` method (sub-agent analysis with an adversarial false-positive pass, threshold ≥ 8). **Report:** [security-review-phase2-20261002.md](security-review-phase2-20261002.md)

**Security controls added in Phase 2:**

| ID | Control | Where |
|----|---------|-------|
| C-01 | `RequestNumber` whitelist `^[A-Za-z0-9-]+$`, 3–20 chars: no LIKE wildcards or junk reach the DB | `RequestSearchQuery` |
| C-02 | Sort column from an enum whitelist; undefined numeric enum values rejected (`Enum.IsDefined`) | `RequestSearchQuery.Validate` |
| C-03 | `PageSize` bounded 1–100; cursor length ≤ 1000 | `RequestSearchQuery` |
| C-04 | Cursor bound to sortBy/sortDir, malformed → 400; the permission filter applies regardless of cursor contents | `RequestCursor`, `RequestRepository` |
| C-05 | Client sends identity headers only to same-origin `/api` | `identity.interceptor.ts` |

**Verified over HTTP:** 9 invalid inputs (short number, bogus/numeric status, unknown sortBy, pageSize 0, from > to, bad date, garbage cursor, cursor from another sort) → 400 with a field-keyed `errors` map; no header → 401.

**Skill findings:** none at or above the reporting bar.
**Considered and dropped:** forged cursor within one's own rows (by design); 400-before-401 ordering for anonymous bad requests (no data revealed, documented).
**Carried forward:** B-S2 (accepted).

## Run 3 — Phase 3 delivery: UI redesign (2026-10-02)
**Scope:** `git diff feat/phase-2-search..feat/phase-3-ui-redesign -- frontend` (PR #3). **Skill:** `security-review` criteria. The full diff was read directly; given the size of this styling-only change, there was no sub-agent pass. **Report:** [security-review-phase3-20261002.md](security-review-phase3-20261002.md)

| ID | Severity | Finding | Status | Fix |
|----|----------|---------|--------|-----|
| F-001 | Low | Google Fonts: visitor IP/User-Agent sent to Google on every load (privacy/GDPR); external CSS with no SRI possible | Open, user decision | Self-host the font file (also fixes performance F-001) |

**Skill findings at or above the bar:** none. Checked: interpolation-only output, attribute bindings (`data-status` is a CSS hook only), no new HTTP, storage or redirects.
**Carried forward:** B-S2 (accepted).
