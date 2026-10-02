# Session Log

Summary of every user input and assistant output, in order.

---

## 1. Review the assignment and the repo
**User:** Asked Claude to review the assignment PDF (`מבחן מקצועי.pdf`) and the source in this folder: (1) confirm it understands the assignment, (2) give a short brief on the repo.

**Assistant:** Read the 4-page PDF and every source file.
- **Assignment:** 3h limit. Part A: Requests search/filter (partial RequestNumber, multi-Status, date range, type, sorting, invalid-input handling), server-side permissions (user = owned/assigned, admin = all), millions of rows, Angular/React UI with loading/error/empty states, optional tests. Part B: microservices design with reliable notifications while Notifications service may be down. Part C: cloud deployment sketch. Required: README.md, AI-usage.md. Simple > complete; must explain every line.
- **Repo:** .NET 8 clean architecture (Domain / Application / Infrastructure / Api) + xUnit tests. EF Core in-memory DB seeded with 500 rows; single `GET /api/requests`; identity via `X-User-Id` / `X-Is-Admin` headers. No frontend, README, `.sln` or git. .NET 8 SDK is installed.
- **Problems found:** (1) whole table loaded into memory, filtering done in memory; (2) no paging; (3) headers can be spoofed, and a missing user ID defaults to user 1; (4) no indexes (in-memory provider); (5) `RequestService` registered in Infrastructure DI; (6) the tests' fake repository is tied to `GetAllAsync`.
- **Suggested direction:** a `RequestSearchQuery` object passed to a repository that builds the `IQueryable`, returning `PagedResult<RequestDto>`; validate input and whitelist sort fields.

## 2. Set ground rules (no code changes yet)
**User:** Set three ground rules before implementation: (1) summarize every chat input and output to a file for later review; (2) create CLAUDE.md via project init; (3) save to memory: run `/performance-reviewer:performance-reviewer` and `/security-review` on every delivery (each major feature, and at project completion).

**Assistant:** Created `SESSION-LOG.md` (this file), created `CLAUDE.md` (commands, architecture, known weaknesses, working rules), and saved both rules to persistent memory. No source code changed.

## 3. Coding style: ponytail full
**User:** Asked for `/ponytail:ponytail full`: simple code that is still performant and efficient.

**Assistant:** Turned on ponytail at full level and added it to the working rules in `CLAUDE.md`. Ponytail means the smallest code that works: no unrequested abstractions, stdlib and installed dependencies first, and a `ponytail:` comment on any deliberate shortcut. Its limits: validation, security and performance-critical paths are never simplified away. No source code changed.

## 4. Backend plan: handling millions of records
**User:** Asked how the backend will handle millions of records, and what else can be done beyond pagination and indexes on frequently queried columns.

**Assistant:** Proposed this plan (no code yet):
1. Do all the work in the database: permissions, filters, sorting and paging are combined into one EF `IQueryable`, so the database returns only the rows of the current page.
2. Select only the DTO columns (`.Select()`), so no full entities and no change tracking.
3. Avoid an expensive total count: fetch pageSize+1 rows to know whether a next page exists, instead of `COUNT(*)` over millions.
4. Paging style: page numbers (OFFSET) are simple but slow for very deep pages; keyset ("seek") paging stays fast at any depth but can't jump to page N.
5. Order matches indexes: composite indexes such as (OwnerId, CreatedAt) and (AssignedToUserId, CreatedAt), with Id as a tie-breaker for stable sorting. The `Owner OR Assigned` condition is the hardest part for indexes.
6. Partial Request Number search: "contains" can't use a normal index. Options: "starts with" (uses the index), a minimum input length, or a trigram index in Postgres.
7. Limit the input: maximum page size, a whitelist of sort fields, cancellation passed to the database.
8. The in-memory database can't show any of this (no SQL, no indexes): it needs a real database (SQLite or Postgres).
9. For a much larger scale (README only): table partitioning by date, read replicas, a separate search engine.
Open decisions for the user: which database, which paging style, total count or not, and how partial search should match.

## 5. Keep the in-memory DB; build the performance measures without a real database
**User:** Suggested showing the performance measures in code rather than standing up a real database environment, and asked whether performant code can still be written while keeping the in-memory DB.

**Assistant:** Agreed; the spec says "assume millions of records", which is a design requirement, not a load test. The code will be written exactly as it would be for a SQL database:
- One combined `IQueryable` (permissions, filters, sort, page), so no `ToListAsync` until the end.
- `.Select` into DTOs; fetch pageSize+1 rows instead of counting.
- Indexes declared with `HasIndex` in `OnModelCreating`. The in-memory provider ignores them, but they are part of the model and would become a migration once a SQL provider replaces the one line `UseInMemoryDatabase`.
- Only operators that translate to SQL (`StartsWith`/`Contains` → `LIKE`, plain comparisons for keyset).
Caveats: the in-memory provider compares strings case-sensitively (search input is uppercased to match `REQ-…`), and real performance can't be measured, which the README will state. The open decisions (paging style, total count, search matching) still stand.

## 6. Decisions + plan + .gitignore
**User:** Chose keyset paging, no total count, and "contains" search with a 3-character minimum. Asked for a plan with Phase 1 (fix the current code) and Phase 2 (build the new feature), and a .gitignore (git repo already initialized).

**Assistant:** Wrote `PLAN.md`:
- Phase 1: add a .sln; move permission filtering into the query with projection to DTOs; 401 for a missing/invalid user ID; move the service registration to Program.cs; declare indexes with `HasIndex`; ProblemDetails error handling; tests rewritten against the real repository + in-memory EF.
- Phase 2: GET with query-string filters; `RequestSearchQuery` validated with data annotations and `IValidatableObject`; opaque keyset cursor tied to sortBy/sortDir; repository combines permission → filters → keyset → order → Take(n+1) → Select; 5 compound indexes; 4 meaningful tests.
Added a short `.gitignore` (bin/obj, node_modules/dist, .DS_Store, .claude/settings.local.json). No source code changed, nothing committed.

## 7. Two PRs
**User:** Delivery will be 2 PRs: one for the Phase 1 fixes, one for the Phase 2 implementation.

**Assistant:** Added a "Delivery" section to `PLAN.md`: a baseline commit on `main` (original code + docs), PR 1 from `fix/phase-1-current-code`, and PR 2 from `feat/phase-2-search` stacked on PR 1. Each PR requires passing build and tests, then performance-reviewer and security-review. Not committed yet; waiting for the user's go-ahead and remote details.

## 8. Baseline commit
**User:** Approved the baseline commit, keeping the logs in the repo. Do not start implementation yet.

**Assistant:** Committed the baseline on `main` (original code + `.gitignore`, `CLAUDE.md`, `PLAN.md`, `SESSION-LOG.md`). No remote yet, no branches created, no code changes.

## 9. Change-annotation rule
**User:** Every code change must keep the old code commented out with an explanation of why it is replaced, followed by the new code with a short explanation of why it is there and how it fixes the bug or meets the requirement.

**Assistant:** Defined the format `// [OLD] Replaced: <why>` + commented old code, then `// [NEW] <why better>` + new code. New files get a `[NEW]` header; deleted code stays as an `[OLD]` block; `//` comments only; the tags are greppable for cleanup later. Added it to `CLAUDE.md` working rules and to memory. No code changes.

## 10. Error handling by environment
**User:** For the global error handling task (1.6): production must never return sensitive data or exception details; development should show exceptions to help debugging. This must be noted in the code as well.

**Assistant:** Updated PLAN.md 1.6: `AddProblemDetails()` everywhere; `UseExceptionHandler()` only outside Development, which gives a generic 500 ProblemDetails with only a traceId and logs the exception on the server. Development uses the built-in Developer Exception Page (full exception + stack trace as ProblemDetails JSON). Fails closed: if the environment is unset, it defaults to Production. 400 validation errors list only the caller's invalid fields. The behavior will be documented in a `[NEW]` comment in Program.cs. No code changed yet.
