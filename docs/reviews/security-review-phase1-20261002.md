# Security Review — Phase 1 (`fix/phase-1-current-code` vs `main`)

**Result: no vulnerabilities at or above the reporting bar (confidence ≥ 8).**

Checked:
- **Identity (`RequestsController`)** — a missing, non-numeric, non-positive or duplicated `X-User-Id` → 401. This closes the old "anonymous = user 1" impersonation. `X-Is-Admin` parsing is unchanged.
- **Permission filter (`RequestRepository`)** — same predicate as before, now in the DB query. `AssignedToUserId = @u` never matches NULL, so there's no null bypass. Parameterized LINQ and no raw SQL, so no injection. The DTO exposes the same fields as before.
- **Error handling (`Program.cs`)** — outside Development, a generic 500 with only `traceId`, verified with a throwing endpoint: the secret message appeared only in the server log. The developer exception page is Development-only. An unset environment means Production (fails closed). Swagger is Development-only.
- **DI move, index declarations** — no security effect.

Known pre-existing risk (not introduced by this PR): `X-Is-Admin` is client-controlled, so any caller can claim admin. This is the exercise's stand-in for real auth. Fix: derive the user id and role from a validated JWT (`[Authorize]`, `User.Claims`). Documented in code (`ponytail:` comment) and to go in the README.
