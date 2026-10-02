# Frontend Plan (Angular search screen)

Plan only. Backend contract = PLAN.md "Phase 2 -> API". The dev proxy removes the need for CORS.

> **Orchestrator review notes (corrections applied below):**
> 1. **Enums serialize as numbers today** — no `JsonStringEnumConverter` is registered, so `status: 2`, not `"InProgress"`. The UI types assume strings. **Backend dependency (added to PLAN.md Phase 2):** register `JsonStringEnumConverter` in `AddControllers().AddJsonOptions(...)` — one line, and it also makes Swagger and the API readable. Query-string binding already accepts names (`status=New`).
> 2. **Empty later page is possible:** rows can be deleted or reassigned between Next clicks, so a page > 0 can come back empty. Show "No more results" and keep Previous enabled, not the "no match" message.
> 3. **Time zone:** `createdAt` from the in-memory seed has a `Z` suffix, but a real SQL provider returns `DateTimeKind.Unspecified` (no `Z`). Always format with `DatePipe` using the `'UTC'` timezone argument so display doesn't depend on the suffix.
Tooling found: Node 22.22.2, npm 10.9.7, `ng` on PATH = Angular CLI 21.2.11. Plan targets that version; re-check the generated defaults after `ng new`.

## 1. Decisions (each with the rejected alternative)

| # | Decision | Why | Rejected |
|---|---|---|---|
| 1 | Angular 21 (installed CLI), standalone components (default, no NgModules), **signals** for state, `HttpClient` with a single RxJS `switchMap` only for the search stream. Folder `frontend/` at repo root | Zero extra setup; signals are enough for one page; sibling of `src/` and `tests/` | NgRx / RxJS-heavy store: far more code than one page justifies. NgModules: legacy |
| 2 | Plain HTML + small hand-written CSS (one `styles.css`) | The spec asks for form + table + states; native `<select multiple>`/checkboxes, `<input type=date>` cover it. No install, no theming to explain | Angular Material: +dependency, +theme, +CDK; heavy for one screen |
| 3 | Dev-server proxy: `proxy.conf.json` maps `/api` -> `http://localhost:60702` (HTTP port, avoids dev-cert trouble). UI calls relative `/api/requests` | Backend has no CORS today; proxy = same origin, zero backend change, nothing to review for security | Enabling CORS in the API: backend change, widens the attack surface, needs a policy to justify |
| 11 | URL query-param sync: **No** | Adds a second source of truth and parse/validate code (cursor is opaque, stack can't be restored). Not in the spec. README "unfinished" note | Sync via `Router` queryParams: shareable links, but +~40 lines and cursor-stack edge cases |
| 13 | Tests: Angular 21 default runner (Vitest via `ng test`; if the scaffold shows Karma/Jasmine, use that instead) | Zero config | Jest / Playwright e2e: extra setup, beyond a 3h budget |

State: a single `signal` for `rows`, `loading`, `error`, `cursorStack`, `sort`. No NgRx, no service-held global state except identity.

## 2. Files (all new, each gets `// [NEW] <why>` header; HTML/CSS use the equivalent comment)

```
frontend/
  proxy.conf.json                       dev proxy /api -> http://localhost:60702
  src/app/
    app.config.ts                       provideHttpClient(withInterceptors([identityInterceptor]))
    app.ts / app.html                   shell: <app-identity-bar/> + <app-request-search/>  (CLI-generated names kept)
    identity.service.ts                 two signals: userId (default 1), isAdmin (false); persisted to localStorage (try/catch)
    identity.interceptor.ts             functional interceptor: adds X-User-Id, X-Is-Admin to every /api request
    identity-bar.component.ts           user id number input + "admin" checkbox -> IdentityService (inline template)
    requests.api.ts                     RequestsApi.search(params) -> HttpClient.get<Page>; builds HttpParams; types
    request-search.component.ts/.html   the page: filter form, table, pager, states, cursor stack, sorting
    request-search.component.css        small layout styles
    problem.ts                          toProblem(HttpErrorResponse) -> { kind, message, fieldErrors, traceId }
    *.spec.ts                           tests (section 9)
```
Seven logic files plus specs. `types` (Page, RequestDto, enum string unions) live at the top of `requests.api.ts`, no separate models file.
Rejected: a facade/store service, separate table/pager/filter child components (only one consumer each, nothing to reuse; add if the page grows).

Types mirror the contract exactly:
`RequestDto { id, requestNumber, customerId, ownerId, assignedToUserId: number|null, status, requestType, createdAt }`,
`Page { items: RequestDto[]; nextCursor: string|null; hasMore: boolean }`,
`Status = 'New'|'InProgress'|'Completed'|'Cancelled'`, `RequestType = 'General'|'Legal'|'Payment'|'Appeal'`,
`SortBy = 'createdAt'|'requestNumber'|'status'|'requestType'`, `SortDir = 'asc'|'desc'`.

## 3. Filter form (Reactive Forms, `FormBuilder.nonNullable`)

| Control | Element | Validation (mirrors backend) | Query param |
|---|---|---|---|
| requestNumber | `<input type=text maxlength=20>` | `minLength(3)` `maxLength(20)` only when non-empty (empty = no filter) | `requestNumber` |
| status | 4 checkboxes (Status list) -> `FormControl<Status[]>` | none; empty = all | repeated `status=New&status=InProgress` (`HttpParams.append`) |
| requestType | `<select>` with "Any" + 4 values | none | `requestType` |
| createdFrom / createdTo | `<input type=date>` | group validator: from <= to when both set | `createdFrom`, `createdTo` (value `yyyy-MM-dd` as-is, inclusive, UTC date) |
| pageSize | `<select>` 10/25/50/100 | within 1-100 by construction | `pageSize` (default 25) |

- Trigger: explicit **Search** submit button (+ Enter) and **Reset**. Debounce-as-you-type rejected: with a 3-char minimum and mixed controls it fires partial/invalid requests and complicates the "reset paging" rule; a button is simpler to explain and to test.
- Invalid form: Search stays enabled but `submit` marks all touched and shows per-field messages; no request is sent. Backend validation remains authoritative (client rules are UX only).
- Empty strings/empty arrays are omitted from params (never sent as `requestNumber=`).

## 4. Search, sort, pagination (one code path)

State: `applied` (filters snapshot taken at Search time, so editing the form does not silently change paging), `sort {by, dir}` (default `createdAt`/`desc`, matching backend defaults), `cursorStack: (string|null)[]` where `stack[i]` is the cursor used to fetch page i (`stack[0] = null`), `pageIndex`.

Algorithm:
- `load(cursor)`: params = applied + sort + pageSize + `cursor` (omit if null) -> GET -> `rows = items`, `nextCursor = res.nextCursor`, `hasMore = res.hasMore`.
- **Search / Reset / sort change / pageSize change**: snapshot filters, `cursorStack = [null]`, `pageIndex = 0`, `load(null)`.
- **Next** (enabled when `hasMore && !loading`): `cursorStack = [...stack.slice(0, pageIndex+1), nextCursor]`, `pageIndex++`, `load(nextCursor)`.
- **Previous** (enabled when `pageIndex > 0 && !loading`): `pageIndex--`, `load(stack[pageIndex])`. Refetches the earlier page with its saved cursor (cheap, always fresh, no caching needed).
- Pager shows "Page N", no total (backend has none). Cursor is treated as an opaque string, never parsed.
- A 400 on a stale/mismatched cursor (e.g. after sort change) cannot happen because sort/filter changes reset the stack first; if it does, show the error and keep the previous rows.

Sorting: header `<button>` inside each sortable `<th>` for `createdAt`, `requestNumber`, `status`, `requestType` (exactly the backend whitelist). Click same column toggles asc/desc; click another column sets it with `desc` for `createdAt`, `asc` otherwise. `aria-sort="ascending|descending|none"` on the `<th>`. Columns Id/Customer/Owner/Assigned are not sortable (not in whitelist).

## 5. States

| State | Behavior |
|---|---|
| Loading | `loading` signal; Search/Prev/Next/sort buttons `disabled` (blocks double-submit); table dimmed with `aria-busy="true"`; text "Loading..." in the live region |
| Success | table + pager |
| Empty | `items.length === 0 && pageIndex === 0` -> "No requests match your filters." (+ Reset hint). On a later page (data changed between clicks) -> "No more results", Previous stays enabled |
| 400 | `problem.errors` (keys are field names, case-insensitive match to control names, e.g. `RequestNumber`, `CreatedFrom`) are set via `control.setErrors({server: msg})` and shown beside the field; unknown keys fall into a general banner. Previous rows are cleared |
| 401 | Banner: "Set a valid user id in the identity bar." (also triggered if the id box is empty/NaN: interceptor omits the header, backend answers 401) |
| 5xx / network (status 0) | Generic "Something went wrong, try again." + `traceId` from the ProblemDetails body when present (network failure: "Cannot reach the server") |

`toProblem()` is the single mapper (HttpErrorResponse -> `{status, message, fieldErrors, traceId}`), unit tested. Error banner has `role="alert"`.

## 6. Race conditions / cancellation

Each load is pushed through a `Subject<LoadRequest>` -> `switchMap(req => api.search(req).pipe(catchError(...)))` subscribed once (`takeUntilDestroyed`); `catchError` inside the inner pipe so an error does not kill the stream. A newer search cancels the in-flight HTTP request and an older response can never overwrite a newer one. Rejected: manual request-id counter (more code, no real cancellation) and `toSignal(toObservable(...))` (hides the paging logic).

## 7. Identity

`IdentityService` holds `userId` (number, default 1 so the demo works on first load; seed owners are 1-5) and `isAdmin`. `identityInterceptor` (functional, `HttpInterceptorFn`) clones the request with `X-User-Id` and `X-Is-Admin` (`'true'|'false'`); skipped if userId is not a finite integer (-> real 401 path, which the UI hints on). Changing identity triggers a fresh Search from page 1 (an effect on the two signals, or the bar calls `search.reset()`; simplest: `(change)` on the bar emits and the page re-runs `load(null)`). Documented as a demo stand-in for real auth, mirroring the backend note.

## 8. Accessibility basics

- Every control has a `<label for>`; status checkboxes in `<fieldset><legend>Status</legend>`.
- Real `<table>` with `<caption class="sr-only">`, `<thead>`, `<th scope="col">`; sortable headers contain `<button>` + `aria-sort`.
- One `aria-live="polite"` region announces "Loading", "N results on this page", "No results"; error banner `role="alert"`; field errors linked via `aria-describedby` and `aria-invalid`.
- Visible focus styles (do not remove outlines); dates shown with `DatePipe`; status shown as text (not color-only).

## 9. Tests (4, Vitest/Jasmine style; `TestBed` + `HttpTestingController`)

1. `requests.api`: params mapping omits empty values, sends repeated `status=New&status=InProgress`, includes `sortBy/sortDir/pageSize/cursor`.
2. `request-search`: cursor stack - Next pushes `nextCursor`, Previous re-requests the earlier cursor (`null` for page 1), and Search/sort change resets to page 0 with no cursor.
3. `toProblem`: 400 with `errors` map -> field errors; 401 -> hint kind; 500 -> generic + traceId; status 0 -> network.
4. Form validator: requestNumber of 2 chars invalid, `createdFrom > createdTo` invalid, empty requestNumber valid.
(Optional 5th if time: a second `search()` cancels the first in-flight request.)

## 10. Run instructions

```bash
cd frontend && npm install                 # first time
dotnet run --project src/Requests.Api      # terminal 1 (http://localhost:60702)
cd frontend && npm start                   # terminal 2: ng serve --proxy-config proxy.conf.json -> http://localhost:4200
npm test                                   # ng test (add --watch=false for CI)
```
`npm start` script is edited to include the proxy flag (or `"proxyConfig"` in `angular.json` serve options) so a plain `ng serve` works.

## 11. Effort (target 65 min)

| Step | Min |
|---|---|
| `ng new frontend` (standalone, CSS, no SSR), proxy config, strip boilerplate, `[NEW]` headers | 8 |
| Types + `RequestsApi` + identity service/interceptor/bar | 10 |
| Search component: form, validation, submit/reset | 12 |
| Table, sorting headers, cursor-stack pager, switchMap pipeline | 15 |
| States + `toProblem` mapping + a11y pass | 10 |
| 4 tests | 8 |
| Manual run-through against the live API (user switch, admin, tied sorts, 400/401) | 7 |
| **Total** | **70** |

## 12. Open questions (recommended default)

| Question | Default |
|---|---|
| Pin Angular 21 (installed CLI) or an LTS major? | Use installed 21; mention version in README |
| Page-size selector or fixed 25? | Selector (10/25/50/100); costs ~3 lines, demonstrates the `pageSize` bound |
| Default identity on first load? | userId 1, non-admin (works immediately with seeded data) |
| Keep `frontend/` inside this repo? | Yes, same repo, separate folder, one README section |
| Dates: send `yyyy-MM-dd` as-is? | Yes; backend treats them as inclusive UTC dates, no timezone conversion in the client |
| Display `createdAt` in local time or UTC? | UTC with explicit label, to match filter semantics |

## 13. Risks

- Vitest vs Karma default in the generated Angular 21 project is assumed; verify after scaffold (fallback noted).
- Proxy covers dev only; a production build would need same-origin hosting or CORS (README note, out of scope).
- Header identity is client-controlled (stand-in); UI must not imply real security.
- Backend 400 `errors` key casing (`RequestNumber` vs `requestNumber`) is unverified until Phase 2 exists: the mapper matches case-insensitively.
- Phase 2 backend must exist before end-to-end testing; until then develop against `HttpTestingController` or a temporary stub.
