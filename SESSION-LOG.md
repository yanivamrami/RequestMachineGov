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

## 11. Phase 1 implementation
**User:** Keep the `[OLD]` comments in the final submission; start Phase 1; the repo is published (origin: github.com/yanivamrami/RequestMachineGov).

**Assistant:** On branch `fix/phase-1-current-code`:
- Added `CandidateTest.sln`.
- `IRequestRepository.GetAllAsync` → `GetVisibleAsync(userId, isAdmin)`: permission WHERE, newest-first order (Id tie-breaker), `.Select` to DTO, all in one query. `RequestService` delegates to it.
- Controller: `[FromHeader]` identity (visible in Swagger); a missing/invalid/non-positive `X-User-Id` → 401.
- Service registration moved to Program.cs.
- 5 indexes plus `RequestNumber` max length 20, unique.
- `AddProblemDetails` with traceId; `UseExceptionHandler` outside Development only.
- Tests rewritten against the real repository + EF in-memory (3 tests: admin, exact visible set incl. null assignee, ordering).
Every change is annotated `[OLD]`/`[NEW]`.
Verified: build clean, 3/3 tests pass. Smoke tests in Production and Development: 401 without a header, user 1 = 186 rows, admin = 500. A temporary throwing endpoint confirmed Production returns a generic 500 and Development the full exception, with the exception logged on the server in both. That check found the production 500 missing `traceId`, fixed with `CustomizeProblemDetails`.
Reviews (saved in `docs/reviews/`):
- Performance: F-001 unbounded result (deferred to Phase 2, by plan), F-002 the OR predicate's index usage needs verification on a real DB, F-003 index write cost (low).
- Security: no findings ≥ 8 confidence; the known pre-existing risk is that `X-Is-Admin` can be spoofed.
Committed locally (2 commits on the branch). Not pushed; no PR yet.

## 12. Orchestrator: Angular UI planning agent
**User:** `/agent-orchestrator` — while the main agent runs Phase 1, start another agent to plan the Angular UI per the spec.

**Assistant:** Phase 1 was already complete (committed locally, awaiting push/PR approval). Created a task list (T1 Phase 1: Review; T2 Angular UI plan: Delegated; T3 Phase 2: Pending). Spawned a background Sonnet agent (well-defined planning task, reviewed by the orchestrator) to write `PLAN-FRONTEND.md` only, with no code or installs. Its brief: plan against the Phase 2 API contract, covering the UI library choice, dev proxy vs CORS, components, filter validation, sorting, the cursor-stack Prev/Next, loading/error/empty states, request cancellation, a user/admin header control, accessibility, 3–5 tests, and effort ≤ ~75 min.

## 13. Review trail logs
**User:** For both the performance and security reviews, keep a LOG file per review type with findings and fixes, as a trail of what was done.

**Assistant:** Created `docs/reviews/PERFORMANCE-LOG.md` and `docs/reviews/SECURITY-LOG.md` (append-only; each run lists scope, commit, findings, status Fixed/Deferred/Accepted/Open, and the fix). Backfilled them with Run 0 (findings on the original code, mostly fixed in Phase 1) and Run 1 (the Phase 1 reviews, plus the traceId issue found during verification). Added the rule to CLAUDE.md and memory. Committed on the Phase 1 branch.

## 14. Angular UI plan delivered and reviewed
**Agent (Sonnet) → orchestrator:** Wrote `PLAN-FRONTEND.md`: Angular 21 (installed CLI 21.2.11, Node 22), standalone + signals, one `switchMap` search stream, plain HTML/CSS (no Material), dev proxy `/api → :60702` (no CORS), single page component + API service + identity interceptor/bar + `toProblem` error mapper, explicit Search button, cursor stack Prev/Next, 400 field-error mapping / 401 hint / 5xx + traceId, a11y basics, 4 tests, ~70 min.

**Orchestrator review (assistant):** Checked the plan against the API contract and found 3 corrections, applied to the plan:
1. The backend serializes enums as numbers (no `JsonStringEnumConverter`), but the UI expects strings. Added a one-line backend item to PLAN.md Phase 2 (#6).
2. A later page can be empty if data changes between clicks; show "No more results".
3. Format dates with DatePipe in 'UTC', because a real SQL provider returns DateTimes without the `Z` suffix.
Task T2 → Completed. Plans not committed yet (uncommitted on the phase-1 branch; they belong with Phase 2).

## 15. Build the client while Phase 2 is reviewed
**User:** PR 1 waits for the user's review. Start an agent to build the client side per the plan while we review the Phase 2 plan.

**Assistant:** Created branch `feat/phase-2-search` (stacked on the Phase 1 branch) and committed the plan files there. Spawned a background Sonnet agent to implement `frontend/` per PLAN-FRONTEND.md, including the review corrections. It writes only inside `frontend/` (no backend, no git), develops against `HttpTestingController` because the Phase 2 API doesn't exist yet, and must report real build and test results.

## 16. Phase 2 Q&A: [FromQuery] vs [FromBody]; why a cursor
**User:** (1) Why `[FromQuery]` and not a `[FromBody]` object? (2) Why do we need the cursor and what is it for?

**Assistant:**
1. A search is a read, so it should be a GET. A body on GET has no defined meaning in HTTP: browser `fetch` and Angular's `HttpClient.get` can't send one, and proxies may drop it. `[FromBody]` would force `POST /search`, losing safe/idempotent semantics, caching, bookmarkable URLs and safe retries. The filters are small scalars, far below URL length limits, and contain no sensitive data. POST-search is the right choice only for large or nested criteria or sensitive values.
2. The cursor is the keyset-pagination bookmark: the last row's sort value + Id, encoded as an opaque token. OFFSET makes the DB read and throw away every skipped row (page 40,000 reads 1M rows) and shifts rows when data changes, causing duplicates or skipped rows. Keyset turns "next page" into an index seek `WHERE (CreatedAt, Id) < (@last, @lastId)`, so each page costs the same at any depth and stays stable while data changes. The token is opaque so clients pass it back unchanged, and it carries sortBy/sortDir so a mismatched cursor is rejected. Trade-off: no jumping to page N and no total-page count, which we accepted.

## 17. Client delivered and reviewed
**Agent (Sonnet) → orchestrator:** Built `frontend/` (Angular 21.2, zoneless, Vitest) per PLAN-FRONTEND.md. `ng build` OK; `ng test` 4/4 passing. Deviations: serverErrors signal instead of setErrors (avoids stale errors blocking submit); invalid user id sends no identity headers at all; a failed Next clears rows (Previous still works); the generated 20 KB welcome page was deleted with a one-line [OLD] note; JSON files can't hold [NEW] comments; no cancellation test. A fresh npm install needed `--legacy-peer-deps` (npm 10.9 bug).

**Orchestrator review (assistant):** Re-ran build and tests myself (passing, 4/4). Confirmed no files outside `frontend/` changed, every source file is annotated, no innerHTML or bypassSecurity, node_modules/dist/.angular are git-ignored. Read the core logic: the cursor stack, switchMap + inner catchError, the identity interceptor, the 400 field mapping and the empty-page states are correct. One fix: added `frontend/.npmrc` (`legacy-peer-deps=true`) so a reviewer's plain `npm install` works (verified). Accepted the agent's deviations. Open dependency for Phase 2: the 400 `errors` keys must be plain property names (e.g. `RequestNumber`) for field-level display. Committed on `feat/phase-2-search`.

## 2026-10-02 — UI redesign (/impeccable)
**User:** Redesign the Requests page with /impeccable — simple demo, but looks count; easy to use, tidy, welcoming.
**Assistant:** Picked "Warm & calm" direction (user chose it). Rewrote styles.css with OKLCH tokens (warm paper, white cards, deep-teal accent, Figtree font); header with intro + "Viewing as" identity pill; filters in one card on a responsive grid, status as checkbox chips, Reset/Search right-aligned; results card with progress bar, sort arrows, colored status pills, "Unassigned" label, friendlier dates, teaching empty state with "Clear filters"; page size moved to the pager footer ("Rows per page"). Added `label()` helper ('InProgress' -> 'In progress'). Logic/validation/a11y bindings unchanged; old markup/CSS kept as [OLD] blocks. Verified in browser at 1280px and 375px (data, empty, validation states); detector clean; `ng test` 4/4 pass. Root cause of the screenshot's error banner: API wasn't running on :60702 (proxy target).

## 18. Phase 2 backend + PR stack
**User:** Agreed with `[FromQuery]` (search is a GET). Start Phase 2 backend as planned, as a PR stacked on the existing one. Mid-turn: "where did you create PR1? I don't see it."

**Assistant:**
- PR 1 had never been opened: I had misread "PR1 will wait for my review" as "don't open it". Pushed `fix/phase-1-current-code` and opened PR #1 → main, bound to the session.
- Implemented Phase 2 on `feat/phase-2-search`:
  - `RequestSearchQuery` ([FromQuery]; data annotations + IValidatableObject; regex whitelist; enum whitelist with `Enum.IsDefined`).
  - `RequestCursor` (hex JSON of the last row's 4 sort values + Id, bound to sortBy/sortDir, unsigned by design).
  - `RequestPage` (items/nextCursor/hasMore).
  - Repository `SearchAsync`: permission → filters → keyset → ORDER BY (key, Id) → Take(n+1) → DTO.
  - Service: n+1 → hasMore + nextCursor.
  - Controller [FromQuery]; JsonStringEnumConverter.
  All changes annotated [OLD]/[NEW].
- Tests 22/22: keyset walk for all 8 sort/direction combinations with ties, combined filters, 9 validation cases plus a valid one, and the Phase 1 tests ported.
- Mutation check: deliberately breaking the Status tie-breaker still passed with page size 4 (data aligned exactly with group size). Fixed the test with page size 3, and the same mutation now fails.
- HTTP smoke test: paging, filters and string enums OK; 9 invalid inputs → 400 with PascalCase field keys (match the client's case-insensitive mapping); no header → 401.
- Performance review: fixed F-003 by rewriting the keyset as the index-seekable `key <= v AND (key < v OR Id < id)`. Accepted with reasons: admin sort by status/type lacks a (key, Id) index; the permission OR needs a per-page sort; LIKE contains; double cursor decode; template method calls.
- Security review: no findings ≥ 8; added a controls table to SECURITY-LOG.
- Noticed ~330 lines of uncommitted styling edits in `frontend/` that I didn't make. Left them uncommitted and out of PR 2.

## 19. PR 3: frontend redesign
**User:** Checking the client themselves for now. Commit the frontend changes as PR 3.

**Assistant:** Reviewed the uncommitted `frontend/` diff (7 files, +333/-72): a "warm & calm" redesign with a header + "Viewing as" identity panel, restyled form/table, a status display label ('InProgress' → 'In progress', API value unchanged), and the Figtree font from Google Fonts. Already annotated [OLD]/[NEW]. Verified: no unsafe HTML; `ng build` OK; `ng test` 4/4. Committed on `feat/phase-3-ui-redesign` (stacked on Phase 2) and opened PR 3 against `feat/phase-2-search`.

## 20. Reviews on PR 3
**User:** Run both reviews on PR 3.

**Assistant:** Read the full PR 3 diff (8 files).
- Performance (Angular checklist): F-001 Sev 3, the Google Fonts stylesheet is render-blocking. F-002 Sev 3, template helper calls (accepted).
- Security: nothing at or above the bar; one Low item, F-001: Google Fonts sends the visitor's IP to Google (privacy/GDPR, no SRI possible).
Both Google Fonts items have the same fix, self-hosting the font, and are logged as "Open, user decision" since the font was the user's design choice. Reports and log entries added under docs/reviews/; committed and pushed to PR 3.

## 21. Part B: microservices architecture
**User:** Review the spec and focus on the microservices architecture. Proposed an MQ for the major services, with an idempotency check so each message is sent and consumed once, as the way to handle a service being down. Asked for the service layout, the APIs between services, and the messaging queue with retry and resilience.

**Assistant:** Wrote `docs/architecture/microservices.md`. Kept the MQ + idempotent-consumer idea and added two corrections:
1. A queue alone loses events on the producer side: a commit followed by a crash or a broker outage means the event never gets published (dual write). Fixed with a transactional outbox.
2. True exactly-once doesn't exist; the design is at-least-once delivery plus an inbox dedupe on `messageId`.

The doc covers:
- Service layout with a mermaid diagram, one DB per service. Sync REST only for validations and lookups; events for reactions. JWT at the gateway, authorization inside each service.
- An API/events table per service.
- A sequence diagram for status change → notification.
- The event envelope, including `version` for out-of-order delivery.
- Idempotency at each hop. The email send has a remaining duplicate gap, which is accepted.
- Retry policy: immediate retries → delayed redelivery → DLQ; poison messages go straight to the DLQ. Polly standard handler for sync calls.
- A failure-scenario table, plus the tech choice: RabbitMQ / Service Bus with MassTransit outbox/inbox, not Kafka.
- The README "one decision": outbox vs. 4 alternatives.

No code changed, so no performance/security review was run.

## 22. Animated data-flow page
**User:** Create an HTML page that shows the data flow between the microservices in both success and failure states, with animation showing the retry mechanism at work.

**Assistant:** Wrote `docs/architecture/data-flow.html`: one self-contained file (vanilla JS + SVG, no dependencies), with light and dark themes and a speed control. It has 7 animated scenarios:
1. Happy path
2. Notifications down: the queue buffers, then drains
3. Broker down: the outbox piles up, the relay retries with backoff
4. Email failing: 3 immediate tries → delayed redelivery 1m/5m → success
5. Retries exhausted → DLQ + alert → operator replay
6. Duplicate publish → dedupe skip
7. Customers down → backoff → circuit breaker open, fail fast, half-open, closed

A live retry-ladder strip and a step log track progress. All 7 scenarios were run in the browser pane and end clean (outbox/queues/DLQ = 0, one email each).

Also fixed a bug in my own design doc: a plain inbox row committed before the email send would make a redelivery after a failed send look like a duplicate, so the email would be lost. The Notification row (Pending/Sent) is now the dedupe record for Notifications. The inbox stays for DB-only consumers like Reporting.

## 23. Part C: AWS cloud deployment
**User:** Plan the Part C cloud deployment for massive scale: (1) review the requirements, (2) explain how the deployment answers the main components, (3) provide an HTML diagram of the AWS deployment.

**Assistant:** Extracted Part C from the spec PDF. It asks for a short sketch on AWS/Azure/GCP covering Compute, DB, Messaging, Monitoring and Scaling; IaC is not required.

Wrote `docs/architecture/cloud-aws.md`:
- Requirements table and scale assumptions (hundreds of millions of rows, ~5k req/s, 10k events/s bursts).
- Per-component answers:
  - Edge: CloudFront + WAF + S3 SPA, Cognito JWT validated in each service.
  - Compute: ECS Fargate across 3 AZs behind an ALB, Service Connect for sync calls.
  - DB: Aurora PostgreSQL + RDS Proxy; Requests reads on replicas, monthly partitions, pg_trgm. DynamoDB conditional put for Notifications dedupe; S3 with pre-signed URLs; Redis for idempotency keys.
  - Messaging: SNS → SQS per consumer; the retry ladder mapped to ChangeMessageVisibility, redrive maxReceiveCount=5 → DLQ; MassTransit SQS transport.
  - Monitoring: CloudWatch, OTel → X-Ray, alarms mapped to the Part B failure scenarios.
  - Scaling: ALB request count per target for APIs, SQS backlog per task for workers, Aurora reader auto-scaling.
  - DR (Aurora Global DB, active-passive) and security.
- Decision: ECS Fargate vs EKS / Lambda / EC2.
- Next steps: OpenSearch, Aurora Limitless, analytics lake.

Wrote `docs/architecture/cloud-aws.html`: an SVG diagram (region → VPC → public/app/data subnets, ECS cluster, regional managed services), with flow-highlight buttons (user request, event → notification, data access, observability), hover tooltips, and summary cards per component. Checked in the browser at 1440px; fixed two overlapping group labels.

## 24. PR 5
**User:** Commit this as PR 5.

**Assistant:** Committed cloud-aws.md/html + session log on `feat/phase-5-cloud` and opened PR 5 against `feat/phase-4-architecture`.

## 25. Is AWS API Gateway used?
**User:** Are we using AWS API Gateway in this architecture?

**Assistant:** No. The Part B "API Gateway" is a role:
- ALB does routing and TLS.
- CloudFront + WAF + Shield do rate limiting and protection.
- Cognito issues the JWT, which each service validates.
- Authorization happens inside each service.

Skipped API Gateway because of per-request cost at billions of calls/month and because none of its features are needed. Would add it for a public third-party API (keys, quotas) or Lambda backends.

## 26. Wouldn't API Gateway perform better?
**User:** Doesn't a dedicated AWS API Gateway perform better than the current architecture?

**Assistant:** No, it would be slightly slower:
1. It's an extra hop (API Gateway → VPC Link → LB → service), in front of the ALB rather than replacing it.
2. Latency lives in the services and DB, which API Gateway doesn't touch.
3. Edge-optimized endpoints are just CloudFront, which we already have.
4. It has a default ~10k req/s regional quota.

Its response cache doesn't fit permission-filtered results: a per-user key gives a low hit rate, and a wrong key leaks data.

## 27. Document the API Gateway decision
**User:** Add it to cloud-aws.md and push to PR 5.

**Assistant:** Added section 4 "Decision: no AWS API Gateway (ALB + WAF instead)": the role mapping table, cost reasoning, five performance points, and when to add API Gateway. Linked it from the Compute section; renumbered "Left out on purpose" to section 5. Pushed to PR 5.
