# Requests — search & filter (candidate test)

A search API and an Angular UI for Requests (Part A), a microservices design with reliable notifications (Part B), and an AWS deployment plan (Part C).

| Part | Where |
|---|---|
| A — Backend | `src/` (.NET 8, clean architecture) + `tests/` |
| A — Frontend | `frontend/` (Angular 21) |
| B — Microservices architecture | [docs/architecture/microservices.md](docs/architecture/microservices.md) + diagram [microservices-architecture.html](docs/architecture/microservices-architecture.html) + animated flow [data-flow.html](docs/architecture/data-flow.html) |
| C — Cloud deployment (AWS) | [docs/architecture/cloud-aws.md](docs/architecture/cloud-aws.md) + diagram [cloud-aws.html](docs/architecture/cloud-aws.html) |
| AI usage | [AI-usage.md](AI-usage.md) |
| Review trail | [docs/reviews/](docs/reviews/): performance & security logs and reports per phase |
| Plans | [docs/plans/](docs/plans/): the backend plan (`PLAN.md`) and the Angular UI plan (`PLAN-FRONTEND.md`) written before coding |

The `.html` diagrams are self-contained. Open them directly in a browser; no server needed.

---

## How to run

**Prerequisites:** .NET 8 SDK, Node 22+ (npm 10).

### Backend
```bash
dotnet run --project src/Requests.Api
```
- API: `http://localhost:60702` (and `https://localhost:60701`). Swagger UI: `https://localhost:60701/swagger` (Development only).
- On startup it seeds 500 deterministic requests into an EF Core in-memory DB. Owners are users 1–5; assignees are users 1–5; every 7th request is unassigned.

### Frontend
```bash
cd frontend
npm install
npm start
```
Open `http://localhost:4200`. The dev server proxies `/api` to `http://localhost:60702` (`proxy.conf.json`), so there is no CORS setup. Start the backend first.

Use the **"Viewing as"** panel to switch the user ID (1–5) or toggle Admin. It sets the `X-User-Id` / `X-Is-Admin` headers on every call.

### Tests
```bash
dotnet test                 # backend: 23 tests
cd frontend && npm test     # frontend: 5 tests (Vitest)
```

---

## The API

`GET /api/requests` — all parameters are optional, in the query string:

| Parameter | Meaning |
|---|---|
| `requestNumber` | Partial match ("contains"), 3–20 characters, letters/digits/`-` only |
| `status` | One or more: `status=Open&status=InProgress` |
| `requestType` | One type |
| `createdFrom`, `createdTo` | Date range (`yyyy-MM-dd`), inclusive; `from` must be ≤ `to` |
| `sortBy` / `sortDir` | `CreatedAt` (default) · `RequestNumber` · `Status` · `RequestType` / `Asc` · `Desc` (default) |
| `pageSize` | 1–100, default 25 |
| `cursor` | The `nextCursor` from the previous page |

Headers: `X-User-Id` (required, positive int) and `X-Is-Admin` (`true`/`false`).

Responses:
- `200 { items, nextCursor, hasMore }`
- `400` ProblemDetails, with the invalid fields listed
- `401` when the user header is missing or invalid
- `500` generic ProblemDetails with a `traceId` only; no exception details outside Development

Example:
```bash
curl -H "X-User-Id: 1" "http://localhost:60702/api/requests?requestNumber=REQ-00&status=Open&status=InProgress&sortBy=CreatedAt&sortDir=Desc&pageSize=10"
```

---

## Technology choices and why

| Choice | Why |
|---|---|
| **Kept the provided .NET 8 / EF Core / clean architecture** | The spec supplies it. I fixed its weaknesses rather than replacing it. |
| **EF Core in-memory provider (kept)** | The spec says "assume millions of records", which is a design requirement, not a load test. The code is written exactly as for SQL: one composed `IQueryable`, `HasIndex` declarations, only SQL-translatable operators. Swapping `UseInMemoryDatabase` for a SQL provider is a one-line change plus a migration. |
| **Keyset (cursor) pagination** | See the decision below. |
| **`[FromQuery]` GET search** | A search is a read: safe, cacheable, bookmarkable and retryable. A body on GET has no defined meaning, and the filters are small scalars. |
| **Data annotations + `IValidatableObject`** | Built into ASP.NET Core, and produce standard 400 ProblemDetails automatically. Enum and sort-field whitelists come from `Enum.IsDefined`. |
| **`JsonStringEnumConverter`** | The API speaks `"InProgress"`, not `1`; the client is readable and stable if enum order changes. |
| **Angular 21**: standalone components, signals, zoneless, plain CSS | The current Angular defaults. No UI library: a filter form and a table don't need one. A single `switchMap` search stream cancels in-flight requests when a new search starts. |
| **Vitest** | The Angular 21 default test runner. |
| **npm `overrides` for `piscina` 5.3.2** | Even the latest `@angular/build` pins `piscina@5.2.0` (critical advisory GHSA-67c8-pqhq-4rmx, build-time only). The override pins the patched version within the same major; build and tests are verified with it. Remove it once Angular ships the fix. |

## Technical decision: keyset pagination (vs. alternatives)

| Option | Problem at millions of rows |
|---|---|
| OFFSET / page numbers | The DB reads and throws away every skipped row (page 40,000 reads 1M rows). Rows shift when data changes, so users see duplicates or miss rows. |
| OFFSET + `COUNT(*)` for "page X of Y" | Adds a full count over millions of permission-filtered rows on every search. |
| **Keyset (chosen)** | "Next page" is a seek: `WHERE key <= @v AND (key < @v OR Id < @id)`. Where an index matches the sort (`CreatedAt`, and a regular user's rows through the permission indexes), every page costs the same at any depth. Pages also stay stable while data changes. We fetch `pageSize + 1` rows to know whether there is a next page, so no count is needed. |

**Trade-offs accepted:**
- No "jump to page N" and no total count. The UI offers Previous/Next using a stack of cursors.
- Keyset limits how many rows each page *returns*. It only guarantees an index seek when an index matches the sort. An **admin** sorting by `Status` or `RequestType` has no `(key, Id)` index, so on a real DB each page is a scan + top-N sort. This is accepted and logged (performance Run 2 F-001); add those two indexes if admin usage shows it.

The cursor is an opaque token holding the last row's sort value + Id. It is bound to `sortBy`/`sortDir`, so a cursor from another sort is rejected with 400. It is not signed: it only moves the position within rows the user is already allowed to see, and permissions are applied in the same query.

Parts B and C each have their own decision section: the transactional outbox, ECS Fargate, and no AWS API Gateway.

---

## How Part A meets the requirements

- **Everything happens in the DB, in one query:** permission → filters → keyset → `ORDER BY key, Id` → `Take(pageSize + 1)` → `Select` into the DTO. No `ToList` until the end, no entity tracking, no total count.
- **Permissions are server-side:** a regular user gets `OwnerId = u OR AssignedToUserId = u`; an admin gets no filter. The client never sends a "who can see what" rule.
- **Indexes** (declared in `RequestsDbContext`): `(OwnerId, CreatedAt, Id)`, `(AssignedToUserId, CreatedAt, Id)`, `(Status, CreatedAt, Id)`, `(CreatedAt, Id)`, and a unique `RequestNumber`.
- **Invalid input** returns 400 with the field names: too-short or illegal `requestNumber`, unknown enum values, reversed date range, `pageSize` out of range, and a bad or mismatched cursor.
- **UI states:** loading (progress bar, and the form stays usable), error (field errors from the 400 response; 401 hint; 5xx with `traceId`), empty ("no results" with "Clear filters"; "no more results" on a later page), and sortable column headers with `aria-sort`.

## Assumptions

- **Identity:** header identity (`X-User-Id`, `X-Is-Admin`) is the stand-in the spec provides. Any caller can claim admin. This is accepted for the exercise (security log B-S2) and marked in code. In production, identity comes from a validated JWT (`[Authorize]`, claims). Part C uses Cognito.
- **"Partial RequestNumber"** means *contains*, with a minimum of 3 characters so the filter is selective. It is matched case-insensitively by uppercasing the input, because request numbers are stored as `REQ-…`.
- **Date range** is by calendar date (`DateOnly`), inclusive on both ends, in UTC.
- **"Assigned to"** means `AssignedToUserId`; unassigned requests are visible to their owner and to admins only.
- **Default sort** is newest first; Id breaks ties so the order is stable.
- **No total count** in the UI, which is accepted as the price of keyset paging.
- **Paging under concurrent changes:** Id tie-breaks make the order stable for a given dataset. If a request's `Status`/`RequestType` changes while someone is paging by that column, the row can move across the cursor and be seen twice or skipped. There are no update endpoints in scope, so this is documented, not handled.

---

## What's unfinished and how I'd continue

1. **A real database.** Switch to PostgreSQL (one line + migrations), seed a few million rows and check the plans with `EXPLAIN ANALYZE`. In particular, check that the `OwnerId OR AssignedToUserId` predicate uses both indexes (index union); if not, rewrite it as two keyset queries merged with `UNION ALL`. Add `pg_trgm` for the "contains" search. (Performance log Run 1 F-002, Run 2 F-002/F-004.)
2. **Real authentication.** JWT bearer + claims instead of headers (security log B-S2).
3. **Admin sorting by Status/Type** at scale: add `(Status, Id)` / `(RequestType, Id)` indexes if usage shows it (Run 2 F-001).
4. **Self-host the Figtree font** instead of loading it from Google Fonts. This fixes a render-blocking request and a privacy finding. Left open as the user's design decision.
5. **More tests:** an API-level integration test (`WebApplicationFactory`) for 400/401, and a frontend test for request cancellation.
6. **Parts B and C are designs only.** The next step would be a Notifications service with MassTransit outbox/inbox on RabbitMQ via docker-compose to demonstrate the failure scenarios for real.

## How the work was delivered

The work was delivered as **7 pull requests**, each building on the previous one and merged into `main` in order:

| PR | Contains |
|---|---|
| [#1](https://github.com/yanivamrami/RequestMachineGov/pull/1) | Phase 1: fixes to the code as received (DB-side permission filter, 401 for missing identity, indexes, error handling) |
| [#2](https://github.com/yanivamrami/RequestMachineGov/pull/2) | Phase 2: search backend (filters, sorting, keyset paging, validation) + Angular client |
| [#3](https://github.com/yanivamrami/RequestMachineGov/pull/3) | Phase 3: UI redesign |
| [#4](https://github.com/yanivamrami/RequestMachineGov/pull/4) | Part B: microservices architecture + diagrams |
| [#5](https://github.com/yanivamrami/RequestMachineGov/pull/5) | Part C: AWS cloud deployment + diagram |
| [#6](https://github.com/yanivamrami/RequestMachineGov/pull/6) | Fixes from an independent code review + README and AI-usage |
| [#7](https://github.com/yanivamrami/RequestMachineGov/pull/7) | Comment cleanup + plans moved to `docs/plans/` |

### Reading the PRs: `[OLD]` / `[NEW]` comments

**PRs 1–6 contain `[OLD]` and `[NEW]` comments on purpose.** They make each change understandable to a reviewer who doesn't know the code, without switching between the diff and the original file:

```csharp
// [OLD] Replaced: <why the old code was wrong or insufficient>
// <the old code, kept as a comment>
// [NEW] <what the new code does and how it fixes the problem>
<the new code>
```

- `[OLD]` keeps the replaced code in place, with the reason it was wrong (for example: *"materializes the whole table before any filtering happens"*).
- `[NEW]` explains what the new code does and why it is better.
- A brand-new file starts with a single `[NEW]` line saying why the file exists.

To follow a change, open the PR's **Files changed** tab and read the `[OLD]` → `[NEW]` pairs from top to bottom.

**This is a review aid for this exercise, not a development practice.** In a normal workflow, the version history and the PR diff already hold the old code, and commented-out code in `main` is noise. So PR #7 removed every annotation after the review: the final `main` branch has only short what-and-why comments. A script confirmed that PR #7 changed comments only (the code is identical with comments stripped). The full before/after of every change remains in the merged PRs 1–6.

### Process

- An **independent code review** of PRs 1–3 by a different AI model ([report](docs/reviews/code-review-pr1-pr2-pr3-20261002.md)) found 6 issues our own reviews had missed. All are fixed or explicitly accepted in PR 6.
- After each feature I ran a performance and a security review. Findings and their status (Fixed/Deferred/Accepted/Open) are in [docs/reviews/](docs/reviews/).
- [SESSION-LOG.md](SESSION-LOG.md) records every prompt and response of the AI-assisted session.
