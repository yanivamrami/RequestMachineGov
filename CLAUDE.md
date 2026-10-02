# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Context

Candidate take-home test (spec: `~/Downloads/מבחן מקצועי.pdf`, Hebrew). Time-boxed to 3h. Evaluators prefer simple, clear, well-reasoned solutions over complete ones, and the candidate must be able to explain every line.

- **Part A (code):** search/filter API + UI for Requests — partial RequestNumber, Status (single/multi), CreatedAt range, RequestType, sorting, invalid-input handling. Regular user sees only requests they own or are assigned to; admin sees all; enforced server-side. Must scale to millions of rows. Frontend in Angular or React with filters, sorting, table, loading/error/empty states. Tests optional (meaningful cases).
- **Part B (design):** microservices (Customers, Requests, Notifications, Documents, Reporting); reliable notification on Request create/status change when Notifications may be down.
- **Part C (design):** cloud deployment sketch (compute, DB, messaging, monitoring, scaling). No IaC.
- **Required files:** `README.md` (run instructions, tech choices, assumptions, one decision with alternatives, what's unfinished) and `AI-usage.md` (tools, stages, changes to AI suggestions, wrong suggestions, verification).

## Commands

No `.sln` file — target project files directly.

```bash
dotnet build src/Requests.Api/Requests.Api.csproj
dotnet run --project src/Requests.Api          # https://localhost:60701, Swagger at /swagger
dotnet test tests/Requests.Tests
dotnet test tests/Requests.Tests --filter "FullyQualifiedName~RequestServiceTests.Administrator_CanSeeAllRequests"
```

## Architecture

.NET 8 clean architecture; dependencies point inward: `Api → Infrastructure → Application → Domain`.

- **Domain** — `Request` entity, `RequestStatus` and `RequestType` enums. No dependencies.
- **Application** — `RequestService` (permission filtering + mapping to `RequestDto`) and the `IRequestRepository` abstraction it depends on.
- **Infrastructure** — EF Core **in-memory** provider (`RequestsDbContext`), `RequestRepository`, `DbSeeder` (500 deterministic rows, `Random(42)`, owners 1–5, every 7th unassigned). `AddInfrastructure()` registers the DbContext, repository, *and* `RequestService`.
- **Api** — `RequestsController` (`GET /api/requests`). Seeding runs at startup in `Program.cs`.

Current user identity comes from request headers `X-User-Id` (int; silently defaults to 1 if missing/invalid) and `X-Is-Admin` (`true`/`false`). These are client-controlled — a stand-in for real auth.

Known starting-point weaknesses (relevant to the task):
- `RequestRepository.GetAllAsync` materializes the whole table; permission filtering then happens in memory in `RequestService`. Filters/sort/paging must be pushed into the `IQueryable`.
- No pagination; in-memory provider has no indexes or migrations.
- Tests use a hand-written `FakeRequestRepository` bound to `GetAllAsync`; changing the repository contract breaks them.

## Working rules

- Append a summary of every user input and assistant output to `SESSION-LOG.md` (newest at bottom) for later review.
- On every delivery (each major feature, and at project completion), run the `performance-reviewer:performance-reviewer` and `security-review` skills.
- **Change annotation (required for every code change, so each change can be explained in the interview):** keep the old code commented out above the new code:
  ```csharp
  // [OLD] Replaced: <why the old code is wrong / insufficient>
  // <old code, each line prefixed with //>
  // [NEW] <what this does and how it fixes the bug / meets the requirement>
  <new code>
  ```
  New files get a single `// [NEW] <why this file exists>` header. Deleted code stays as `// [OLD]` blocks. Use `//` line comments only (never `/* */`). The tags are greppable (`grep -rn "\[OLD\]"`) for cleanup before final submission.
- Code style: `ponytail` at **full** — simplest code that works, but still performant (query-side filtering, paging, indexes). Never simplify away validation or server-side authorization.
