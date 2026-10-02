# Performance Review — Phase 3 (`feat/phase-3-ui-redesign` vs `feat/phase-2-search`)

**Stack detected:** Angular 21 (zoneless, signals), CSS
**Checklists applied:** Angular
**Files reviewed:** 7 (full): `index.html`, `styles.css`, `app.html`, `identity-bar.component.ts`, `request-search.component.{html,css,ts}`
**Findings:** 2 — 0 Critical / 0 High / 2 Low

### Executive Summary
A styling-only change with no new data flow, requests or subscriptions. The only load-time cost is the external Google Fonts stylesheet, which blocks rendering. Runtime additions (template helper calls, CSS `:has()`, a loading animation) are cheap at ≤ 100 rows with zoneless change detection.

### Findings Overview

| ID | Sev | Confidence | Category | Location | Title |
|----|-----|-----------|----------|----------|-------|
| F-001 | Sev 3 | Confirmed | Loading – render-blocking resource | `index.html` | Google Fonts stylesheet blocks first paint and needs 2 extra connections |
| F-002 | Sev 3 | Likely | Angular – Change Detection | `request-search.component.html` | `ariaSort(by)` called 3× per sortable header and `label(...)` once per row per check |

### Finding Details

#### F-001 · Sev 3 · Confirmed · Loading – render-blocking resource
**Location:** `frontend/src/index.html`, the `<link rel="stylesheet" href="https://fonts.googleapis.com/...">`
**Description:** A cross-origin stylesheet in `<head>` blocks rendering until DNS, TLS and download complete for `fonts.googleapis.com`, then fonts are fetched from `fonts.gstatic.com`. `preconnect` and `display=swap` reduce the cost but don't remove the block, roughly 100–300 ms on a cold load.
**Fix:** self-host the font (download the woff2 into `public/fonts/`, declare it with `@font-face { font-display: swap; }` in `styles.css`, remove the 3 `<link>` tags). This also resolves security-review F-001.

#### F-002 · Sev 3 · Likely · Angular – Change Detection
**Description:** `ariaSort(by)` runs 3 times per sortable header (4 headers), and `label(s)` runs once per row and chip, per change-detection pass. Zoneless + signals means passes happen only on events and signal changes, with at most 100 rows and trivial functions.
**Fix (only if the page grows):** `@let s = ariaSort(by);` inside the header block, and a pure pipe for `label`.

### No issues found in
- The loading animation only renders while `loading()` is true and respects `prefers-reduced-motion`.
- `:has()` selectors are scoped to small chip groups.
- No new HTTP calls, subscriptions or `@for` loops without track keys (`track s`, `track r.id`, `track c.label`).
- The pager `<select>` binds to the same `FormControl` (no duplicate state).

### Review Limitations
No Lighthouse or Web Vitals measurement was run; the F-001 timing is a typical-range estimate.

---
## Review Metadata
- **Reviewed by:** Claude Performance Reviewer Skill
- **Checklists:** Angular
- **Date:** 2026-10-02
- **Scope:** `git diff feat/phase-2-search..feat/phase-3-ui-redesign -- frontend`
