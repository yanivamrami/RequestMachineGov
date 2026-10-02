# Independent code review: PRs 1–3

Reviewed 2 October 2026. Recommendation: request changes on PRs 2 and 3. PR1 improves the baseline; its demo authentication and persistence assumptions remain production blockers, rather than newly introduced vulnerabilities.

## Scope and context

| PR | Reviewed head | Base | Scope |
| --- | --- | --- | --- |
| [1](https://github.com/yanivamrami/RequestMachineGov/pull/1) | `25315db144c4bfaa940db2cff15fa43baedab997` | `main` | Existing API fixes |
| [2](https://github.com/yanivamrami/RequestMachineGov/pull/2) | `39edea4b1fea9effe6126923751c00f804f5c7af` | PR1 branch | Search API and Angular client |
| [3](https://github.com/yanivamrami/RequestMachineGov/pull/3) | `5782bb06bec52540d93d136f3617de004c262d55` | PR2 branch | Frontend redesign |

GitHub head SHAs matched local remote refs. The current checkout's `src`, `frontend`, and `tests` match PR3, so executable checks cover the combined code from these three PRs. PR-specific changes were reviewed against their stacked bases, not repeatedly against main.

All four pages of the supplied Hebrew assignment were read and rendered. Part A requires partial request-number search, status/date/type filters, sorting, invalid-input handling, owner-or-assignee visibility for ordinary users, admin visibility, server-side enforcement, and an assumption of millions of records. The three-hour exercise favors simple, explained tradeoffs. Parts B and C are outside this code review. Document instructions were treated as assignment context, not authorization to perform unrelated work.

The `[OLD]` and `[NEW]` comments are intentional explanations. They are not findings and should be preserved as requested. No application source or existing user documents were changed, and no GitHub review/comments were posted.

## Findings introduced by the reviewed changes

P2 means an actionable issue to fix in the normal development cycle. Advisory severity is reported separately from application-specific risk.

### F1 — P2 — PR2: vulnerable build dependency is locked into the frontend

Location: [frontend/package-lock.json:6996](/Users/yanivamrami/Downloads/CandidateTest/frontend/package-lock.json:6996), also the Angular build dependency at line 351.

The lockfile installs `piscina@5.2.0` through `@angular/build`. A live `npm audit` reports GHSA-67c8-pqhq-4rmx. It produces two critical entries—Piscina and its affected parent—not two independent vulnerabilities.

The advisory describes worker code execution when a separate weakness has already polluted `Object.prototype`. This is a development/build dependency; I did not establish a prototype-pollution path in this project or an exploit through the deployed browser application. Nevertheless, reproducible builds currently retain a known vulnerable dependency.

Fix: use a compatible Angular build release carrying patched Piscina, or a tested override to a patched version such as 5.3.2. Regenerate the lockfile, rerun the build/tests and audit. Do not blindly apply the audit's suggested major Angular downgrade. [Reviewed advisory and patched versions](https://github.com/advisories/GHSA-67c8-pqhq-4rmx).

### F2 — P2 — PR2: the maximum valid end date causes HTTP 500

Location: [RequestRepository.cs:90](/Users/yanivamrami/Downloads/CandidateTest/src/Requests.Infrastructure/Repositories/RequestRepository.cs:90).

`createdTo=9999-12-31` binds to a valid `DateOnly` and passes validation. `to.AddDays(1)` then overflows before query execution.

Verified against the API in Production:

```text
GET /api/requests?createdTo=9999-12-31
X-User-Id: 1
=> 500 ProblemDetails

GET /api/requests?createdTo=9999-12-30
X-User-Id: 1
=> 200, 25 rows
```

This converts a legitimate boundary input into a server failure and contradicts the invalid-input handling requirement. Fix by explicitly handling the maximum date without addition, or rejecting an unsupported upper date with a field-specific 400. Add an HTTP regression test covering the upper boundary and an ordinary inclusive end date.

### F3 — P2 — PR2: alternate sort orders do not have matching pagination indexes

Locations: [RequestRepository.cs:142](/Users/yanivamrami/Downloads/CandidateTest/src/Requests.Infrastructure/Repositories/RequestRepository.cs:142) and [RequestsDbContext.cs:35](/Users/yanivamrami/Downloads/CandidateTest/src/Requests.Infrastructure/Persistence/RequestsDbContext.cs:35).

The new endpoint supports ordering by `(Status, Id)` and `(RequestType, Id)`. The model has no RequestType index. Its `(Status, CreatedAt, Id)` index is not ordered by `(Status, Id)` because CreatedAt is an unconstrained intervening key. Consequently, a database cannot simply consume that index in the requested status/id order, and type sorting lacks a corresponding access path altogether.

For broad/admin searches at millions of rows, these sorts can require large scans and sorts despite returning only 26 rows. Keyset pagination limits the response and avoids OFFSET, but does not by itself guarantee an index seek or constant page cost. The indexes were introduced in PR1, but PR2 introduces the unsupported query shapes.

Fix: choose a target relational provider, measure the supported query shapes, and add appropriate indexes or reduce the supported sorting contract. Include regular-user OR permissions and selective/nonselective filters in the measurements. Test first and deep pages on representative data. This finding is based on query/index inspection; no production SQL execution plan or million-row benchmark was obtained. [EF Core pagination index guidance](https://learn.microsoft.com/en-us/ef/core/querying/pagination).

### F4 — P2 — PR2: changing identity leaves the previous identity's rows visible

Location: [request-search.component.ts:105](/Users/yanivamrami/Downloads/CandidateTest/frontend/src/app/request-search.component.ts:105), specifically the identity effect and `restart()`.

The identity panel updates immediately and triggers a request, but `restart()` only resets pagination. Existing rows remain in the DOM until the new response arrives. The template dims them but keeps them readable. Switching from admin to an ordinary user therefore temporarily labels the old admin results as belonging to the new viewing context; a stalled request extends that state.

A browser check with controlled HTTP responses confirmed that the panel displayed user 2 while `REQ-PRIVATE-USER1` remained visible during the pending request. This is stale client state, not a new server authorization bypass, and the same browser already possessed the old data.

Fix: clear rows, cursor/hasMore and identity-specific error state immediately when the identity changes. Retaining rows during ordinary paging can remain a separate UX choice. Test a delayed identity-switch response and a failed switch.

### F5 — P2 — PR3: form boundaries have insufficient non-text contrast

Location: [styles.css:67](/Users/yanivamrami/Downloads/CandidateTest/frontend/src/styles.css:67), using the tokens at lines 15 and 19.

Unfocused text/date inputs use `--line-strong: oklch(85% .014 70)` against `--surface: oklch(99.6% .003 80)`. Converting those colors to relative luminance yields approximately **1.57:1**. The input background and surrounding card use the same surface color, so the border is the boundary cue. This makes fields difficult to identify for users with low vision and falls below the applicable 3:1 non-text contrast requirement.

Fix: use a darker control-border token that reaches at least 3:1 against the surrounding surface. Keep decorative card separators separate so they need not all become darker. Check default, hover, focus and error states. [W3C non-text contrast guidance](https://www.w3.org/WAI/WCAG21/Understanding/non-text-contrast.html).

### F6 — P2 — PR3: pagination controls are clipped at a 320px viewport

Location: [request-search.component.css:117](/Users/yanivamrami/Downloads/CandidateTest/frontend/src/app/request-search.component.css:117), together with `.results { overflow: hidden; }`.

The navigation group is a nonwrapping flex row with fixed gaps, button padding and a minimum page-label width. At 320 CSS pixels, a browser measurement found a 298.28px navigation row inside a 288px results card, before accounting for the footer's inner padding. The group extended from x=10.86 to x=309.14 while the card extended from x=16 to x=304. Both outer button edges were visibly clipped. Larger page numbers increase pressure.

Fix: allow the navigation group to wrap or stack, or reduce its small-screen spacing and minimum widths. Verify at 320px and at browser zoom, with enabled Previous/Next controls and a multi-digit page number. The table's own horizontal scrolling is appropriate; clipping pager controls is a separate problem.

## Existing or explicitly deferred risks

### High production risk: callers choose their own identity and admin privilege

[RequestsController.cs:67](/Users/yanivamrami/Downloads/CandidateTest/src/Requests.Api/Controllers/RequestsController.cs:67) still trusts `X-User-Id` and `X-Is-Admin`. Verified: user 999999 gets zero rows normally and 100 rows plus a next cursor after adding `X-Is-Admin: true`. Any caller can also claim another user's ID. The server enforces the visibility expression, but the identity/role supplied to it is untrusted.

This is inherited and explicitly documented as demo authentication, so it is not counted as a new PR1 regression. The PDF does not itself grant an authentication exemption. Before real deployment, derive identity and role from validated authentication, require authorization, and disable/gate the demo identity mechanism. Do not describe the current application as production-secure.

### Scalability remains a design intention, not demonstrated runtime behavior

The runtime and tests use EF InMemory. Model indexes are ignored, there are no relational migrations, and filtering/sorting still operate over the in-process store. Moving operators into IQueryable is a useful design improvement but does not turn this provider into an indexed database.

Also, substring request-number search has no search index. An admin no-match query can examine the entire dataset regardless of the page-size cap. Three-character minimum input does not bound that work, and source order in IQueryable does not force a SQL optimizer to apply selective predicates first. A suitable substring index/search strategy and measurements are needed if the millions-of-records requirement is to be demonstrated. These limitations were already acknowledged in code/review notes.

Use relational integration tests for translation, collation, timestamp round trips, index deployment and ordering. InMemory passing results cannot certify SQL behavior. [Microsoft's database testing guidance](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy).

PR1 alone still returns an unbounded list; PR2 resolves response-size bounding. Evaluate the final stack accordingly rather than counting the same deferred issue twice.

## Lower-priority observations

- PR1's claim that every 404 uses ProblemDetails is broader than the implementation: a verified request to an unknown route returned an empty 404. Add status-code-page handling if uniform error bodies are part of the intended public contract, or narrow the claim.
- The PR3 production build passes but warns that component CSS is 4.51kB against a 4kB warning budget. This is a small budget overrun, not evidence of a material runtime bottleneck.
- PR3 adds Google Fonts. The production build fetches/inlines font CSS and generated output still references external font files. Self-hosting would remove that external dependency; the PR already discloses it.
- Stable ordering with Id tie-breakers is correct for static data. Sorting by mutable Status/RequestType cannot promise snapshot-consistent traversal during concurrent changes. There are no mutation endpoints in this reviewed scope, so this is a limitation to document rather than a current reproduced regression.
- The required root README and AI-usage files are absent from the reviewed PR heads but exist as untracked local drafts. Ensure they are included in the final submission. They were not altered during this review.

## Validation and positive findings

| Check | Result |
| --- | --- |
| Backend tests, combined PR3 code | 22/22 passed |
| Frontend tests | 4/4 passed |
| Angular production build | Passed; CSS warning noted above |
| NuGet direct/transitive advisory check | No vulnerable packages reported |
| npm advisory check | Two critical entries for the single Piscina advisory chain |
| API smoke tests | Confirmed 401 without identity, maximum-date 500, adjacent-date success, header-controlled admin access and empty route 404 |
| Headless Chrome, production frontend | Confirmed old rows during pending identity switch and clipped pager at 320px; HTTP responses were controlled for these UI checks |

The repository applies permission predicates before materialization, projects only DTO columns, uses deterministic tie-breaking, caps page size at 100, avoids total-count queries and supports cancellation. The frontend uses switchMap to prevent superseded HTTP responses from overwriting newer results. No raw SQL construction, unsafe HTML trust bypass or committed application credential was found in the reviewed code. Unsigned cursors do not bypass the independently reapplied permission predicate. Production exception responses tested here were generic and included trace IDs.

This was source review plus focused tests, dependency auditing and browser/API checks, not a penetration test or a production load test. No claim is made that every vulnerability or performance issue has been excluded. Fix the six findings, then prioritize real authentication and relational query validation before deploying beyond the demonstration.
