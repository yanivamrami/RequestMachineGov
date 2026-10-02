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
