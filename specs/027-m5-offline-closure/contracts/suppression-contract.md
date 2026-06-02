# Contract: Inline suppression editing (US4)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 4 · **FRs**: FR-018 … FR-022

Two scopes (file-scope dropped — no per-rule file directive exists in the shared format, and inventing one would touch the analyzer parser + engine tests + WPF).

## The shared format (verified against `AkmlSql.Analysis/SuppressionParser.cs`)

| Directive | Effect | Per-rule? |
|---|---|---|
| `-- noqa: RULEID[, RULEID2]` | Suppress the listed rule(s) on the comment's line | yes |
| `-- noqa` | Suppress **all** rules on the line | no |
| `-- noqa-begin` / `-- noqa-end` | Suppress **all** rules in the block | no |

The browser uses **`-- noqa: RULEID`** only (the per-rule, per-line form `FixAction.cs:99` already emits on the WPF side).

## Scope 1 — Suppress on this line (FR-019) — cross-surface

1. From a finding (problems list row and/or editor gutter/lightbulb), choose "Suppress on this line".
2. The browser inserts ` -- noqa: <RuleId>` at the **end of the finding's line** (1-based `CodeIssueInfo.Line` → CodeMirror line end), matching `FixAction.cs`'s append behaviour.
3. Next analysis pass: `SuppressionParser` maps the directive to `SuppressedLines[line] = {RULEID}`; `AnalysisEngine` filters that finding on that line; the rule still fires elsewhere (FR-019).
4. **Cross-surface (FR-022)**: the identical string is honoured by the engine and WPF surface — same parser, same form.

Edge case: if the rule is already globally off, line suppression is a no-op/hint (not a duplicate directive).

## Scope 2 — Suppress globally (FR-020, FR-021) — browser-local + bugfix

1. Choose "Suppress globally" on a finding.
2. The browser writes `RuleOverrides[RuleId] = "off"` into `WebAnalysisSettings` via `IAnalysisSettingsStore.SetAsync` (IndexedDB `AnalysisSettings` store); persists across reload (FR-021).
3. **Bugfix (FR-021, prerequisite)**: `AnalyserService` today constructs `new CodeAnalysisSettings { Enabled = true }` and **ignores** `RuleOverrides`. It MUST instead inject `IAnalysisSettingsStore`, read `RuleOverrides` per analyse, and project them onto `CodeAnalysisSettings`: `"off"` → add to `GloballySuppressedRules`; other values → per-rule severity. `AnalysisEngine.AnalyzeAsync` already filters `GloballySuppressedRules`, so once wired the override takes effect.
4. **Per-surface, not cross-surface**: global suppression is browser-local. The engine/WPF use project `.casettings`, which the web edition deliberately does not read (`IAnalysisSettingsStore` docstring). FR-022's cross-surface guarantee applies to **line** scope only; global is explicitly per-surface.

## Entry points (FR-018)

- `ProblemsListComponent` row: an action (context menu or inline buttons) offering "Suppress on this line" / "Suppress globally".
- Optionally the editor location (lightbulb/gutter) — the problems-list entry is the minimum; editor-location is a nice-to-have if the CM lint integration makes it cheap.

## Test contract

`tests/AkmlSql.Web.Tests/Analysis/SuppressionEditTests.cs`:

- line suppression inserts exactly `-- noqa: RULEID` at the right line; a re-analyse drops that finding but keeps the rule elsewhere;
- the inserted string parses under `SuppressionParser` (use the real parser to prove cross-surface) — guards the format contract;
- global override persists and, **after the E7 bugfix**, suppresses the rule on the next analyse;
- global-then-line is a no-op (no duplicate directive).

## Out of scope

- **File-scope-per-rule** (`-- noqa-file: RULEID`) — would be a new shared directive across three surfaces. Named follow-up.
- Reading project `.casettings` in the browser — the web edition stays IndexedDB-only for settings (unchanged decision).
