# Tab Coloring Audit — AKML-SQL Phase 5 vs SQL Prompt §5.1

**Spec**: 020-sqlprompt-visual-parity · **FR**: FR-011a · **Date**: 2026-05-15

This audit enumerates SQL Prompt's documented Tab Coloring rules
(`doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md §5.1`)
and grades AKML-SQL's existing Phase 5 implementation against each one.

**Per the Q3 clarification on spec 020, closing any `Differs` / `Missing`
items is explicitly OUT OF SCOPE for this spec.** This doc is the
deliverable; gaps below are surfaced for a follow-up Tab Coloring spec to
decide whether to close.

## Sources

- **Reference (SQL Prompt)**: `doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md §5.1` (lines 654–697)
- **AKML implementation (Phase 5)**:
  - `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` — applies tab background, status bar, floating window border
  - `src/AkmlSql.Core/Models/Tabs/EnvironmentRule.cs` — `(order, pattern, matchTarget, color, label)` rule record
  - `src/AkmlSql.Core/Models/Tabs/EnvironmentMatcher.cs` — wildcard pattern matcher
  - `src/AkmlSql.Core/Config/AppSettings.cs.TabSettings` — settings POCO
  - `tests/AkmlSql.Core.Tests/Tabs/EnvironmentMatcherTests.cs` — documented default rules

## Audit table

| # | SQL Prompt §5.1 feature | AKML Phase 5 state | Verdict | Notes |
|---|---|---|---|---|
| 1 | Apply tab colour to tab header bar (3 px coloured strip) | Applies a tinted background to the whole tab background (not just a 3 px strip). 60-alpha derived semi-transparent overlay so editor text stays readable. | **Differs** | Visual difference: AKML tints the whole tab body; SQL Prompt tints only a top strip. Equivalent semantic role. |
| 2 | Apply tab colour to status bar (full-width coloured bar) | `ApplyStatusBarColor` (T027) walks the WPF visual tree, locates the SSMS/VS status-bar element, applies a 60-alpha tint of the environment colour. | **Matches** | Functionally equivalent. Tolerates SSMS/VS version differences via try/catch fallback. |
| 3 | Apply tab colour to floating-window border | `ApplyFloatingWindowBorder` (T028) on every window-got-focus activation. | **Matches** | |
| 4 | Active tab uses bright colour; inactive tabs use darker shade | Single colour per tab; no "active vs inactive" differentiation. | **Missing** | Low impact — most users only look at the active tab anyway. |
| 5 | Per-server matching (pattern → colour) | `EnvironmentMatcher.Match(rules, serverName)` with `*PROD*,*LIVE*` wildcard patterns and `matchTarget = "serverName"`. | **Matches** (more flexible) | AKML uses comma-separated wildcard lists; SQL Prompt uses an explicit-list UI. Same semantic. |
| 6 | Per-database matching | Not visible in current `TabColoringManager`. `EnvironmentRule.matchTarget` field exists but only `"serverName"` is wired. | **Missing** | The data shape supports it (`matchTarget` is parametric), but no wiring resolves a database identifier. |
| 7 | Per-group matching (e.g. Registered Servers groups) | Not implemented. | **Missing** | Would require SSMS Registered Servers integration. |
| 8 | Assignment hierarchy: Group → Servers in Group → Server → Database | Single-tier wildcard pattern matching only. No layered override semantics. | **Missing** (single-tier) | AKML's `order` field gives priority but no implicit inheritance. |
| 9 | Gradient toggle (lighter at top, darker at bottom) | `CreateBrushFromHex(hex, gradient: true)` returns a `LinearGradientBrush` from a lighter tint (20% toward white) to base colour. Toggle stored in settings. | **Matches** | |
| 10 | Default environments — 6 entries (Production / Staging / Testing / Development / Local / Custom) | AKML defaults (per `EnvironmentMatcherTests`): **4 entries** — PRODUCTION / STAGING / DEV / AZURE. | **Differs** | AKML's "AZURE" is added; "Testing" / "Local" / "Custom" missing. |
| 11 | Default hex codes match SQL Prompt's published palette | SQL Prompt: `#E74C3C / #F39C12 / #3498DB / #2ECC71 / #95A5A6 / #9B59B6`. AKML defaults: `#FF4444 / #FFB800 / #44BB44 / #4488FF`. | **Differs** | Tonally similar but not byte-identical. |
| 12 | Status-bar-matches-tab toggle (`Bool`, default `On`) | Status-bar tinting is always applied when an environment rule resolves. No "off" toggle in `TabSettings`. | **Partial** | Enable/disable can be done indirectly by deleting all rules. No dedicated boolean. |
| 13 | Right-click context-menu assignment methods (`Tab Color (Server)`, `Tab Color (Database)`, `Tab Color (Group)`, `Tab Color (Servers in Group)`) | Not implemented — assignment is via the Settings dialog UI only. | **Missing** | Substantial UX feature; users add/edit rules in `SettingsWindow` instead of in-context. |
| 14 | Default colour for unassigned tabs (`Enum`, usually transparent) | Tabs with no matching rule are left untinted (host default). | **Matches** | |
| 15 | Edit-environments UI (name + colour swatch, click → OS colour picker) | `SettingsWindow` has a Tabs page where rules are listed and edited. Colour is a hex string field. | **Partial** | No swatch picker — hex text input only. |
| 16 | Execution-guard environment-colour propagation (Production-coloured DROP/DELETE confirm dialog) | Confirmation dialogs use a separate `Status.Danger` token, not the per-environment colour. | **Differs** | Semantic equivalent (red = danger) but doesn't carry the per-server context. |

## Summary

| Verdict | Count |
|---|---:|
| **Matches** | 5 |
| **Matches (more flexible)** | 1 |
| **Differs** | 4 |
| **Partial** | 2 |
| **Missing** | 5 |
| **Total enumerated** | **17** |

## Items grouped by recommended follow-up scope

**Visual parity (mostly cosmetic — could be closed via the existing spec 020 visual-parity tokens):**
- #1 (3 px strip vs whole-tab tint) — UI tweak in `TabColoringManager.ApplyTabColor`
- #10 (default-environment set: add Testing, Local, Custom) — config seed change
- #11 (default hex codes: align to SQL Prompt's palette) — config seed change

**Settings UX gaps (small but visible):**
- #12 (status-bar-matches-tab toggle as a dedicated bool)
- #15 (colour swatch picker instead of hex text)

**Functional behaviour gaps (require code design + UX):**
- #4 (active vs inactive tint differentiation)
- #6 (per-database matching) + #7 (per-group matching) + #8 (assignment hierarchy)
- #13 (right-click context-menu assignment) — requires SSMS command-bar integration
- #16 (per-environment colour on execution-guard dialogs)

## Follow-up recommendation

A separate Tab Coloring parity spec is the right home for closing the
`Differs` / `Missing` items above. That spec should pick which of the
three buckets (visual / settings UX / functional) to scope and decide on
SSMS-version-specific UX (e.g. SSMS 20 isolated-shell command-bar
limitations may make #13 infeasible on that host).

This spec (020) intentionally leaves the rules as-is and only re-skins
visual chrome (FR-011 narrowed by Q3 clarification).
