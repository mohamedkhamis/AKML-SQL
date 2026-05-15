# Phase 0 — Research: SQL Prompt Visual Parity + Format Gap Closure

**Feature**: `020-sqlprompt-visual-parity`
**Date**: 2026-05-13
**Status**: Complete — all NEEDS CLARIFICATION resolved

This document captures the research decisions that feed Phase 1 design. Each section is one open question from the plan, resolved with Decision / Rationale / Alternatives.

---

## R1. Token coverage — what's already in `ThemeTokens`, what's missing?

**Decision**: Reuse the existing 7 token families (Surface / Text / Border / Accent / Status / Editor / Chat) built in spec 016. Add 4 new families for SQL Prompt parity gaps: `IconBadge.*`, `TabColor.*`, `History.*` semantic markers, `Spacing.*` and `Typography.*` scalars.

**Rationale**:

- Audit of `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeTokens.cs` shows the chrome vocabulary (popup background, border, hover, selection, text primary / secondary / disabled / placeholder / link, accent + hover + pressed, status success / warning / danger / info, editor margin / popup / spinner) already covers what the SQL Prompt Options dialog and History window need at the chrome level.
- What's **missing** is the per-suggestion-type icon palette (12 icon types in `SQL_Prompt_Features_Core.md §1.2`), the tab-coloring swatch palette, the SQL History status icons (open / closed / star / search-match), and explicit `Spacing.*` / `Typography.*` tokens.
- Adding to the existing `ThemeTokens` keeps the SC-001 scanner simple (one allow-list) and lets `HostThemeWatcher` switch every new token via the same single notification.

**Alternatives considered**:

- *New parallel token bank for SQL Prompt parity*: rejected. Duplicates infrastructure; would require the scanner to maintain two allow-lists; doubles the surface area that needs to react to host theme changes.
- *Inline hex values inside per-feature classes*: rejected. Direct violation of FR-004; defeats SC-001.

**New tokens to add (target counts)**:

| Family | Count | Examples |
|---|---|---|
| `IconBadge.*` | 12 | `IconBadge.Table`, `IconBadge.View`, `IconBadge.Column`, `IconBadge.StoredProc`, `IconBadge.Function`, `IconBadge.Snippet`, `IconBadge.Keyword`, `IconBadge.Database`, `IconBadge.Schema`, `IconBadge.Trigger`, `IconBadge.Index`, `IconBadge.Synonym` (each token resolves to a brush; backgrounds use the 20%-alpha variant from the doc, foregrounds use the solid colour) |
| `TabColor.*` | 8 | swatch defaults per SQL Prompt's documented palette |
| `History.*` | 5 | `History.OpenIcon`, `History.ClosedIcon`, `History.Star.Active`, `History.Star.Inactive`, `History.MatchHighlight` |
| `Spacing.*` | 4 | `Spacing.XS = 4`, `S = 8`, `M = 12`, `L = 16` (DIU) |
| `Typography.*` | 4 | `Typography.Chrome` (Segoe UI 12), `Typography.ChromeTitle` (Segoe UI 14 SemiBold), `Typography.Editor` (host editor font), `Typography.IconBadge` (Segoe UI 9 SemiBold) |

Bindings (Light + Dark hex values) come directly from `doc/SQL-PROMPT/` tables.

---

## R2. `.sqlpromptstyle` → `FormatProfile` mapping

**Decision**: One static mapping table in `SqlPromptKeyMap.cs`. Each entry is a (JSON path, AKML field, transform) triple. Round-trip preservation via a `Dictionary<string, JsonElement> _passthrough` on `FormatProfile` for unknown keys.

**Rationale**:

- The `.sqlpromptstyle` schema from `SQL_Prompt_Features_Core.md §2.2` defines ~ 50 settings across 13 groups (`metadata`, `whitespace.newLines`, `lists`, `parentheses`, `casing`, `dml`, `ddl`, `controlFlow`, `cte`, `joins`, `caseExpressions`, `operators`, `inStatements`).
- A declarative map keeps the importer / exporter symmetrical (export iterates the same table) and lets the schema-coverage test enumerate it.
- The `_passthrough` bucket means future SQL Prompt versions can introduce keys we haven't yet mapped without losing data on round-trip (FR-024).

**Alternatives considered**:

- *Imperative per-section import code*: rejected. Asymmetric with export; doubles maintenance.
- *Convert-once on import, drop unknown keys*: rejected. Violates FR-024 round-trip preservation.
- *Native dual-schema profile*: rejected. Too disruptive to existing AKML format users; the spec mandates coexistence.

**Mapping coverage (preview — full table in `data-model.md`)**:

| SQL Prompt JSON path | AKML field | Transform | Status |
|---|---|---|---|
| `metadata.name` | `FormatProfile.Name` | identity | ✅ direct |
| `casing.reservedKeywords` | `FormatProfile.Casing.ReservedKeywords` | enum-name normalise | ✅ direct |
| `casing.builtInDataTypes` | `FormatProfile.Casing.BuiltInDataTypes` | enum-name normalise | ✅ direct |
| `lists.placeCommasBeforeItems` | `FormatProfile.Lists.CommaPlacement` | bool → enum (`true` ↔ `Leading`, `false` ↔ `Trailing`) | ✅ direct |
| `parentheses.collapseShortParenthesisContents` | `FormatProfile.Parens.CollapseShort` | identity | ✅ direct |
| `dml.collapseStatementsShorterThan` | `FormatProfile.Dml.CollapseThreshold` | identity (int) | ✅ direct |
| `ddl.placeFirstProcedureParameterOnNewLine` | `FormatProfile.Ddl.FirstParamOnNewLine` | enum-name normalise | ✅ direct |
| `joins.joinKeywordAlignment` | `FormatProfile.Joins.KeywordAlignment` | enum-name normalise | ⚠ new field in AKML |
| `caseExpressions.whenAlignment` | `FormatProfile.Case.WhenAlignment` | enum-name normalise | ⚠ new field in AKML |
| `operators.alignment` | `FormatProfile.Operators.Alignment` | enum-name normalise | ⚠ new field in AKML |
| `inStatements.alignment` | `FormatProfile.InStatements.Alignment` | enum-name normalise | ⚠ new field in AKML |
| `cte.placeColumnsOnNewLine` | `FormatProfile.Cte.PlaceColumnsOnNewLine` | enum-name normalise | ⚠ new field in AKML |
| `casing.useObjectDefinitionCase` | — | — | ❌ unsupported (surface in "Settings not yet supported" panel) |

`⚠` = AKML field does not exist yet — gap closure task in tasks.md.
`❌` = explicitly out of scope for this feature; pass-through preserves the key on re-export.

---

## R3. Live preview latency under IPC

**Decision**: Reuse existing `FormatPreview` IPC (msg 12 / 112). Debounce 100 ms on the UI thread before sending; cancellation semantics — a newer request supersedes an in-flight one, and the in-flight response is discarded on arrival. Target ≤ 250 ms p95 from setting-change to preview-rendered.

**Rationale**:

- The existing `FormatPreview` round-trip was measured in the spec 014 Phase 3 work at ~ 60 ms for a 200-line sample on dev hardware (engine warm). Adding 100 ms UI debounce and ~ 30 ms WPF render still leaves 60 ms slack against the 250 ms budget.
- Debounce of 100 ms is the established human-input cadence (typing pauses are ≥ 100 ms 95 % of the time); ensures one rapid setting-toggle doesn't queue 5 requests.
- Cancellation by request supersession (rather than engine-side cancellation tokens) keeps the engine code unchanged and is sufficient: the editor only ever cares about the latest preview.

**Alternatives considered**:

- *In-shell formatter mirror for preview*: rejected. Duplicates the 7-stage pipeline; defeats the process-boundary architecture; means preview can drift from real formatter output.
- *No debounce, fire-on-every-change*: rejected. Stalls the engine under rapid sliders / number-spinner changes.
- *Server-side cancellation*: rejected. Adds engine complexity for negligible benefit; the in-flight request finishes in tens of ms anyway.

---

## R4. Format Styles editor — new window or embed in Options dialog?

**Decision**: New modal window (`FormatStylesEditorWindow`). Launched from `Options → Format → Styles → "Edit Formatting Styles…"` button.

**Rationale**:

- Matches SQL Prompt's UX (`SQL_Prompt_Options_Dialog.md §8`): the Options dialog has a single "Edit Formatting Styles" button that opens a larger separate window for the three-panel editor.
- The editor needs three independently-scrollable vertical panels (style list, settings tree, settings + live preview). Cramming this inside the Options content panel (which is ~ 660 px wide given a ~ 220 px tree nav) leaves no room for a meaningful preview pane.
- Lets the user keep Options open behind the editor and return to it on close.

**Alternatives considered**:

- *Embed in Options content panel*: rejected (above).
- *Tool window (dockable)*: rejected. SQL Prompt uses a modal — making it a tool window would diverge from parity and complicate the live preview lifecycle.

---

## R5. Formatter pipeline coverage — what's implemented, what's a gap

**Decision**: 30 of ~ 50 SQL Prompt settings already covered by the existing `FormatterPipeline`. 15 require new layout rules. 5 are explicitly unsupported for this feature.

**Rationale**:

- `FormatterPipeline` (7 stages, see `doc/formatting.md`) has handlers for casing, comma placement, basic whitespace, parenthesis indent. Gaps are concentrated in the alignment / collapse-threshold settings introduced by SQL Prompt's later versions.
- Gap closure is mechanical — each setting maps to an `IAstAnnotator` or `ILayoutRule` in the pipeline. No architectural change needed.

**Gap matrix (full version in `data-model.md`)**:

| Setting group | Implemented | Gap |
|---|---|---|
| Whitespace | Tab size, tab behavior, wrap column, blank-lines-between | preserve-empty-lines-after-batch-separator |
| Lists | Comma placement, space-after-comma, align-aliases (partial) | align-items-across-clauses, place-subsequent-items-on-new-lines |
| Parentheses | indent, collapse-short, opening / closing on new line, add-spaces | collapse-shorter-than (threshold is hardcoded today) |
| Casing | reserved keywords, built-in functions, built-in data types, global vars | use-object-definition-case (deferred — not in scope) |
| DML | place-clauses-on-new-line, INTO on new line | collapse-short-statements, collapse-short-subqueries, right-align-clauses |
| DDL | basic CREATE TABLE alignment | align-data-types-and-constraints, place-first-procedure-parameter, collapse thresholds |
| JOINs | place-on-condition-on-new-line | join-keyword-alignment (4 variants), on-condition-indentation, insert-empty-line-before-join |
| CASE | basic WHEN/THEN layout | place-first-WHEN-on-new-line (enum), WHEN-alignment (enum), place-expression-on-new-line |
| Operators | inline | alignment (3 variants), place-BETWEEN-keyword-on-new-line |
| IN | inline | alignment (3 variants) |
| CTE | basic | place-columns-on-new-line (enum) |

Each "gap" cell → one task in `tasks.md`.

**Alternatives considered**:

- *All-or-nothing — block release until every setting is matched*: rejected. The 30 already covered are the most-used; shipping P1+P2 with 30/50 + clearly listing the remaining gaps in the "Settings not yet supported" panel meets FR-023 and lets teams move.
- *Drop unsupported settings silently on import*: rejected. FR-022, FR-023 require visibility.

---

## R6. Migration — preserving existing user theme customisations

**Decision**: One-time migration on first launch with the new token set. Writes `%AppData%/AKML SQL/themeMigration.v1.json` marker. If the user has any `legacyColorOverrides` keys in `config.json`, those override the new token defaults and a one-time `InfoBar` is queued on next dialog open.

**Rationale**:

- FR-030 requires existing customisations to win; SC-011 targets 0 regressions in a beta cohort.
- A marker file is the standard atomic-write pattern used elsewhere in the codebase (e.g. update-result handling); idempotent.
- Surfacing the migration via `InfoBar` instead of a modal popup avoids interrupting the user — they see the notice the next time they open something already in our UI.

**Alternatives considered**:

- *Silent migration*: rejected. Even with the override-wins rule, users deserve to know their colours moved into a tokenised system in case they want to migrate themselves.
- *Force-prompt modal on first launch*: rejected. Hostile UX, especially for users who don't customise.

---

## R7. DPI scaling viability

**Decision**: All new WPF sizes specified in DIU. Hardcoded-hex scanner extended to also flag absolute pixel-tuned `Width="…"` / `Height="…"` literals outside an allow-list of well-known small values (e.g. 16, 18, 28). Audit pass on existing surfaces during P2 / P3 tasks.

**Rationale**:

- WPF is DIU-based — 1 DIU = 1/96 inch — and scales automatically at 125 / 150 / 200 % when the per-monitor DPI awareness manifest is set.
- All 6 shell projects already inherit the per-monitor DPI manifest from the VS / SSMS host.
- The risk is *not* the rendering engine but stale literal pixel values left in XAML / `.cs` from earlier work. The scanner catches new occurrences; the audit task catches existing ones.

**Alternatives considered**:

- *Per-DPI lookup tables*: rejected. WPF doesn't need them; would add complexity without value.
- *Trust manual screenshot review*: rejected. Easy to miss at 125 %; SC-005 requires verification at every supported DPI.

---

## R8. Round-trip for unknown JSON keys

**Decision**: `FormatProfile` gains `[JsonExtensionData] public Dictionary<string, JsonElement> _passthrough { get; set; }`. Importer populates it with anything not in `SqlPromptKeyMap`. Exporter writes the dictionary back at the same JSON paths it came from.

**Rationale**:

- `System.Text.Json` supports `[JsonExtensionData]` natively on `netstandard2.0`.
- The bucket is per-section (nested), so a future SQL Prompt v12 key under `joins.foo` lands at `joins.foo` on export, not at the root.
- The exporter is a single serialiser pass — no special-casing.

**Alternatives considered**:

- *Drop unknown keys with a warning*: rejected. Hard fail on FR-024 round-trip.
- *Preserve the raw imported JSON alongside the parsed profile and emit it on export*: rejected. Profiles get edited after import; the edited state must be what's written.

---

## Resolution status

| ID | Question | Resolved? |
|---|---|---|
| R1 | Token coverage | ✅ |
| R2 | `.sqlpromptstyle` mapping | ✅ |
| R3 | Preview latency | ✅ |
| R4 | Editor window vs embed | ✅ |
| R5 | Formatter pipeline gaps | ✅ |
| R6 | Migration of existing customisations | ✅ |
| R7 | DPI scaling | ✅ |
| R8 | Unknown-key round-trip | ✅ |

No NEEDS CLARIFICATION remain. Ready for Phase 1 design.
