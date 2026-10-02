# AI usage

The full prompt-by-prompt record is in [SESSION-LOG.md](SESSION-LOG.md). This file answers the five questions from the spec.

## 1. Which AI tools did I use?

- **Claude Code** (desktop app, Claude Opus model) as the main assistant: reading the spec and repo, planning, writing code, running builds and tests, git and PRs.
- **GPT-6 Astra**, a different AI model used as an **independent reviewer** of PRs 1–3. A second model with no stake in the code catches what the authoring model's own reviews miss (see section 6).
- **Claude sub-agents (Sonnet)**, started from the main session for two well-bounded tasks: writing the Angular UI plan (`PLAN-FRONTEND.md`), and implementing `frontend/` from that plan. The main session reviewed both before I accepted them.
- **Claude Code skills** (packaged instructions):
  - `ponytail`: simplest code that works, without simplifying away validation, security or performance.
  - `performance-reviewer` and `security-review`: run after every feature; results in `docs/reviews/`.
  - `impeccable`: the UI redesign.
  - `agent-orchestrator`: delegating to sub-agents.
- **Project rules I set for the AI** (`CLAUDE.md` + persistent memory):
  - log every turn to `SESSION-LOG.md`;
  - run both reviews on every delivery;
  - annotate every change with `// [OLD]` (why it was wrong) and `// [NEW]` (why it's better), so I can explain every line.

## 2. At which stages?

| Stage | How AI was used |
|---|---|
| Understanding | Summarized the Hebrew spec and audited the provided repo. It found 6 problems: whole-table load, no paging, spoofable headers / missing-user default, no indexes, wrong DI layer, tests tied to `GetAllAsync`. |
| Design / planning | Proposed options for millions of rows. **I made the decisions**: keyset paging, no total count, "contains" with a 3-character minimum, keep the in-memory DB. It then wrote `PLAN.md` / `PLAN-FRONTEND.md` from those decisions. |
| Code | Phase 1 fixes, Phase 2 search backend, the Angular client (sub-agent), the UI redesign. |
| Tests | 22 backend tests (keyset walk for all 8 sort/direction combinations, filters, validation) and 4 frontend tests. |
| Review | Performance and security review after each phase; findings logged with a status and a fix. |
| Independent review | GPT-6 Astra audited PRs 1–3 end to end; Claude verified each finding against the code and fixed them in PR 6 (section 6). |
| Architecture (B, C) | Refined my message-queue idea into outbox + idempotent consumers; the AWS deployment plan; both HTML diagrams. |
| Docs | README, this file, the session log. |

## 3. What did I change from the AI's suggestions, and why?

- **Kept the in-memory DB.** The AI proposed moving to SQLite or Postgres to make indexes real. I chose to keep the provided setup and write the code exactly as it would be for SQL. The requirement is a design for millions of rows, not a benchmark.
- **Chose the paging model myself.** Of the options it laid out (OFFSET vs keyset, count vs no count, prefix vs contains), I picked keyset + no count + contains with a 3-character minimum.
- **Production error handling.** I required that production never returns exception details while Development shows them, and that this is documented in the code. The AI's plan was updated to match.
- **Part B messaging.** My idea was a message queue with an idempotency check. The AI kept it and added the transactional outbox, which is needed because a queue alone loses events if the service crashes between the DB commit and the publish. I accepted the change.
- **Part C:** I asked whether AWS API Gateway would perform better than the ALB. The AI explained why it wouldn't (extra hop, response cache doesn't fit per-user results, default request quota). I had the reasoning documented as a decision in `cloud-aws.md`.
- **Sub-agent output was corrected before acceptance.** The Angular plan:
  - assumed string enums while the API sent numbers, so I added `JsonStringEnumConverter` to the backend;
  - didn't handle an empty later page;
  - formatted dates without UTC.
  For the implementation, I added `.npmrc` so a plain `npm install` works.
- **The Google Fonts finding** (render-blocking + privacy) is left open as my design decision. The fix is known: self-host the font.

## 4. Were there AI suggestions I rejected, or that were wrong?

Rejected:
- Using a real database for this submission (see above).
- `POST /search` with a `[FromBody]` object. Search stays a GET with `[FromQuery]`; the reasoning is in the README.

Wrong suggestions that were caught:
- **A test that couldn't fail.** A mutation check (deliberately breaking the Status tie-breaker) still passed, because page size 4 lined up exactly with the data groups. The test was changed to page size 3, and the same mutation now fails.
- **An index-unfriendly keyset condition.** The performance review flagged the first version (F-003). It was rewritten as the seekable `key <= v AND (key < v OR Id < id)`.
- **A missing `traceId` in the production 500 response.** Found in the smoke test that throws on purpose; fixed with `CustomizeProblemDetails`.
- **A bug in its own Part B design.** The first version committed the dedupe record before sending the email, so a redelivery after a failed send would be skipped and the email lost. It was found while building the retry animation and fixed: the Notification row (Pending/Sent) is the dedupe record.
- **A misunderstanding of my instructions.** It read "PR 1 will wait for my review" as "don't open PR 1"; I corrected it and the PR was opened.

## 5. How did I verify the result is correct?

- **Builds and tests** on every phase: `dotnet build`, `dotnet test` (23/23), `ng build`, `ng test` (5/5). Sub-agent results were re-run by the main session, not taken from the agent's report.
- **Mutation testing** of the keyset tests (above) and of both PR 6 regression tests (each fails with its fix removed), to prove the tests catch the bug they're meant to catch.
- **HTTP smoke tests** against the running API:
  - paging, filters, string enums;
  - 9 invalid inputs → 400 with field names;
  - no header → 401;
  - user 1 sees 186 rows, admin sees 500;
  - Production vs Development error output.
- **Browser checks** of the UI at 1280px and 375px (data, empty, validation states); both architecture HTML pages; all 7 failure scenarios in the animation end in a clean state.
- **Performance and security reviews** after each phase, with every finding tracked to Fixed / Deferred / Accepted / Open in `docs/reviews/`.
- **My own review** of every PR. The `[OLD]`/`[NEW]` annotations exist so that I can explain each change myself, not just trust the AI.

## 6. Independent review by a second AI model (GPT-6 Astra)

After PRs 1–3 were done, I had **GPT-6 Astra** review them independently: source review, dependency audit, and live API/browser checks. Report: [docs/reviews/code-review-pr1-pr2-pr3-20261002.md](docs/reviews/code-review-pr1-pr2-pr3-20261002.md).

**Why:** both earlier review passes were run by the same model that wrote the code. A different model is a cheap way to get a reviewer without the author's blind spots.

**What it found:** six issues, all of which our own performance and security reviews had missed:

| # | Finding | Outcome (PR 6) |
|---|---|---|
| F1 | A build dependency (`piscina`) with a critical advisory | Fixed with an npm override; `npm audit` now 0 |
| F2 | `createdTo=9999-12-31` caused a 500 | Fixed + regression test |
| F3 | Status/Type sorts have no matching index | Kept as an accepted trade-off; README claim corrected |
| F4 | Previous identity's rows visible after switching user | Fixed + regression test |
| F5 | Input borders below WCAG 3:1 contrast | Fixed (3.6:1) |
| F6 | Pager clipped at 320px | Fixed, verified in the browser |

It also noted that 404 responses had an empty body, against what our code comments claimed. That's fixed too.

**How I used it:**
- I didn't accept the report blindly. Claude checked every finding against the code first. All six reproduced, and the contrast figure was recalculated independently (1.57:1).
- I disagreed with one recommendation. F3 is a known, logged trade-off (Run 2 F-001), so it stays accepted; only the README's overstated wording was fixed.
- One proposed fix wouldn't have worked: updating Angular for F1 doesn't help, because even the latest `@angular/build` pins the vulnerable version. An `overrides` entry was needed instead.

**What I learned:** the gaps were the kinds of checks we never ran: a full `npm audit` including dev dependencies, boundary values (not just typical invalid input), accessibility contrast, and the narrowest screen width. They're now part of the verification steps, and the findings are logged as Run 4 in [docs/reviews/](docs/reviews/).
