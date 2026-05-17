# M2 Theme Parity Audit — DEFERRED

**Status: deferred** (spec 021 T036). Needs interactive testing on a workstation with the IDE plugin built and running.

## Why it's deferred

This audit captures Light / Dark / High-Contrast screenshots of:

1. The web edition's Editor page in each mode.
2. The same SSMS/VS plugin surface (editor + Options dialog + Format Styles editor) in the matching theme.

Both halves require:

- A running IDE host (SSMS 22 or VS 2022) with the AkmlSql plugin loaded.
- A running web edition served from `dotnet run --project src/AkmlSql.Web`.
- Cross-window screen capture into a `specs/021-web-edition/SC-006-EVIDENCE/`-style directory.

None of that can be exercised inside the headless CLI session that landed the M2 code. The audit lands when a workstation session can capture both halves side-by-side.

## What the audit gates

The audit informs `wwwroot/css/themes/{light,dark,high-contrast}.css` adjustments. Anything more than a 5 % colour drift from the IDE plugin baseline becomes a follow-up CSS tweak. Token values come from `docs/theme-tokens.json`; the generator is `scripts/generate-theme-css.ps1`.

## Acceptance bar

A single Markdown table per theme listing:

| Element | IDE plugin colour | Web edition colour | Delta | Action |
|---------|-------------------|--------------------|-------|--------|

Plus side-by-side PNG screenshots referenced from the table rows.

## When this can run

Reasonable lift for one focused workstation session: ~3 hours including screen capture, eyeball comparison, and recording the deltas. The CSS adjustments are usually one-token-tweak commits.
