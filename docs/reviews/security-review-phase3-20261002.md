# Security Review — Phase 3 (`feat/phase-3-ui-redesign` vs `feat/phase-2-search`)

**Result: no vulnerabilities at or above the reporting bar (confidence ≥ 8).** One low-severity privacy / third-party item is recorded for a decision.

Checked:
- **Output encoding:** all new markup uses `{{ }}` interpolation and attribute bindings (`[attr.data-status]`, `[attr.aria-*]`). No `innerHTML`, `bypassSecurityTrust*`, or URL bindings. `data-status` takes a server enum value and is used only as a CSS selector hook.
- **Identity bar:** same bindings as before; still a demo stand-in (B-S2).
- **No new HTTP calls, storage or redirects.**

| ID | Severity | Finding |
|----|----------|---------|
| F-001 | Low | **Third-party request to Google Fonts.** Every page load sends the visitor's IP and User-Agent to Google, which is a privacy/GDPR concern (a 2022 Munich court ruling, LG München I, 3 O 17493/20, found dynamically loading Google Fonts without consent unlawful). It's also an external runtime dependency with no Subresource Integrity check possible, since the CSS is dynamic. Fix: self-host the font, which also fixes performance F-001. |
