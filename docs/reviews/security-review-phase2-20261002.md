# Security Review — Phase 2 (`feat/phase-2-search` vs `fix/phase-1-current-code`)

**Result: no vulnerabilities at or above the reporting bar (confidence ≥ 8).** Backend `src`, `frontend/src`, `proxy.conf.json` and `angular.json` were reviewed.

Checked:
- **Authorization:** the permission `WHERE` is applied first and unconditionally for non-admins. Every later step is another `Where` (an AND), so nothing can widen it. The cursor holds only comparison values (no identity), so a forged cursor only moves the start position inside the caller's own rows. That's why it is intentionally unsigned.
- **Injection:** all LINQ, so parameterized, with no raw SQL. `RequestNumber` must match `^[A-Za-z0-9-]+$` (3–20), so no LIKE wildcards. The sort column comes from an enum whitelist, and undefined numeric enum values are rejected.
- **Cursor deserialization:** `System.Text.Json` into a sealed record of primitives and enums, with no polymorphism or `object` members. Malformed input → 400. Length is capped at 1000.
- **Data exposure:** same DTO fields as before. The cursor contains only the caller's own last row. Validation messages are fixed strings. The service's `ArgumentException` falls back to the generic production 500.
- **Client:** no secrets; interpolation only (no `innerHTML` or `bypassSecurityTrust*`); no redirects. Identity headers are attached only to same-origin `/api` URLs. The dev proxy is local only.

Considered and dropped: forged cursor skipping through one's own results (by design); validation 400 before the identity 401 for anonymous callers with bad criteria (no data revealed; documented with a `ponytail:` comment).
Known, pre-existing: client-controlled `X-Is-Admin` (B-S2, accepted).
