# Implementation Plan: SQL Prompt Visual Parity Across All AKML-SQL Surfaces (with Format & Upload Formatter Gap Closure)

**Branch**: `020-sqlprompt-visual-parity` | **Date**: 2026-05-13 (revised 2026-05-15 post-`/speckit.clarify`) | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `D:\Repo\01-Khamis-Projects\AKML-SQL\specs\020-sqlprompt-visual-parity\spec.md`

## Clarifications applied (from `/speckit.clarify`, 2026-05-13)

The 5 Q&A bullets in `spec.md § Clarifications` directly affect this plan. Summary of the deltas:

- **Q1 → SC-007 match rule**: parity comparison normalises trailing whitespace per line, EOL → `\n`, drops UTF-8 BOM, then requires byte-exact equality. Drives the `FormatParityTests` driver in Phase 7.
- **Q2 → built-in styles (FR-027a)**: ship 3 read-only Native styles transcribed from SQL Prompt's documented defaults (Compact, Indented, AlignedLeftBracket). Built-in seeding is part of US2 (Phase 4).
- **Q3 → Tab Coloring scope (FR-011, FR-011a)**: visual parity only — Phase 5's assignment-rule engine is **out of scope**. The Tab Coloring audit doc (FR-011a) is the only Phase 5 deliverable beyond visual chrome re-skin.
- **Q4 → unsupported settings UX (FR-023)**: each unsupported setting renders in the editor tree at its natural group location, control disabled, value visible, "not yet supported" badge. Value lives in `PassthroughUnknownKeys` for round-trip.
- **Q5 → active style scope (FR-027b)**: exactly one active style per user, globally shared across SSMS 20/21/22 + VS 2019/22/26. `AppSettings.FormatterSettings.ActiveProfile` is the single source of truth; never split per-host.

## Summary

Achieve visual parity between AKML-SQL and Redgate SQL Prompt across every visible UI surface (suggestion popup, Options dialog, Format Styles editor, IntelliSense surfaces, SQL History, Code Analysis output, Tab Coloring, AI window, snippet manager, editor margins, tooltips), plus close functional gaps in the SQL formatter — most importantly, importing `.sqlpromptstyle` files so teams who have standardised on SQL Prompt can move to AKML-SQL without losing their house style.

The plan **extends, does not replace**, three existing platforms already in the codebase:

1. **`ThemeTokens` / `ThemeRegistry` / `HostThemeWatcher`** (spec 016) — the centralised token bank. We add SQL Prompt-specific tokens (icon-type badge colours, History status icons, tab-coloring swatches, etc.) and finish the legacy-`ThemeManager` migration where surfaces still hold hardcoded chrome.
2. **`FormatterPipeline`** (7-stage formatter in the .NET 10 engine) and `FormatterSettings` (`AppSettings.cs`) — we expose every SQL Prompt setting through the existing pipeline, and add a `SqlPromptStyleImporter` that round-trips `.sqlpromptstyle` JSON.
3. **IPC channel** — already carries `ProfileImport` (msg 17 / 117) and `FormatPreview` (msg 12 / 112). These are exactly the operations US2 and US5 need. We add one new message: `RequestStyleEditorSchema` (28 / 128) for the Format Styles editor to build its UI from the canonical setting descriptor.

The work breaks naturally into the same priority groups as the spec: P1 (token bank completion + `.sqlpromptstyle` import) ships first, P2 (Options dialog, Format Styles editor, IntelliSense surfaces, live preview parity) second, P3 (History / Tabs / Code Analysis / AI window / margins) third.

## Technical Context

**Language/Version**: C# — `.NET Framework 4.7.2` for shell extensions (LangVersion latest), `netstandard2.0 + net10.0` dual-target for Core, `.NET 10` for the engine and updater.
**Primary Dependencies**: WPF (PresentationFramework, PresentationCore, WindowsBase); MessagePack for IPC; System.Text.Json (netstandard2.0 polyfill); Serilog 4.x; existing `ThemeRegistry` / `ThemeTokens` / `HostThemeWatcher` (spec 016); VS SDK per host (15.9.3 / 16.0.208 / 17.14.*); ScriptDom (TSql170Parser) in the engine.
**Storage**: `%AppData%/AKML SQL/config.json` (settings); `%AppData%/AKML SQL/styles/*.akmlstyle` (native format profiles); new `%AppData%/AKML SQL/styles/imported/*.sqlpromptstyle` for imported SQL Prompt styles; `%AppData%/AKML SQL/logs/*.log` (Serilog).
**Testing**: xunit 2.x for engine + core; a new `tests/format-parity/` corpus (≥ 200 SQL files paired with SQL Prompt golden outputs) for SC-007; manual side-by-side screenshot review per surface for SC-003 / SC-004; automated hardcoded-hex scanner for SC-001; colour-blind simulation harness for SC-012.
**Target Platform**: Windows (Win10 / 11). Hosts: SSMS 20 (VS 2017 IsolatedShell, x86), SSMS 21 / 22 (x64), VS 2019 (x86), VS 2022 (x64), VS 2026 (x64).
**Project Type**: Desktop multi-host IDE extension. Out-of-process `.NET 10` engine for parse / format / analysis; in-process WPF UI per host.
**Performance Goals**: live preview re-render ≤ 250 ms p95 for a 200-line SQL sample (SC-009); theme switch ≤ 1 s end-to-end (SC-002); formatter parity ≥ 95 % byte-equivalent on the corpus (SC-007).
**Constraints**: six shell targets share the same source via `AkmlSql.Shell.Shared.projitems`; both Light and Dark themes mandatory; DPI scaling at 100 %, 125 %, 150 %, 200 % (SC-005); no hardcoded chrome hex (FR-004); WPF brushes frozen, font families hoisted to `static readonly` (CLAUDE.md WPF conventions); existing user customisations win on first launch (FR-030).
**Scale/Scope**: ~ 20 visible WPF surfaces to verify, ~ 50 SQL Prompt format settings to map, 14 documented SQL Prompt SVG mockups as visual contract, ~ 30 FRs, ~ 12 measurable SCs.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Constitution file**: not present (`.specify/memory/constitution.md` does not exist in this repo). No explicit principles to enforce as gates. The plan instead anchors on **CLAUDE.md** conventions (treated as the de-facto project charter), which are summarised below and used as the gate check.

| Gate | Source | Result |
|---|---|---|
| Theme colours come from `ThemeRegistry` — no hardcoded chrome hex | `CLAUDE.md` "WPF UI conventions" | ✅ Enforced as FR-004 and verified by SC-001 scanner |
| Brushes frozen, font families hoisted to `static readonly` | `CLAUDE.md` "WPF UI conventions" | ✅ Existing pattern preserved; new surfaces obey |
| Modal dialogs set `Owner` via DTE HWND before `ShowDialog` | `CLAUDE.md` "WPF UI conventions" | ✅ Format Styles editor follows the pattern |
| FR-005 safety-dialog cancel-must-be-default rule | `CLAUDE.md` "WPF UI conventions" | N/A — this feature does not modify safety dialogs |
| Shell projects built individually with MSBuild (no `dotnet build`) | `CLAUDE.md` "Build Gotchas" | ✅ No change to build process |
| Out-of-process engine; shell ↔ engine only via named pipe | `CLAUDE.md` "Process Boundary" | ✅ All formatting still runs in engine; preview uses existing `FormatPreview` IPC |
| Atomic config writes; `ConfigManager.Load` idempotent and cached | `CLAUDE.md` "Code Conventions" | ✅ Format style files use the same temp-file + rename pattern; cached settings invalidated on `AnalysisSettingsChanged`-style event |
| Path validation via `Path.GetFullPath` canonical check, not `.Contains("..")` | `CLAUDE.md` "Security" | ✅ Style file import path is canonicalised before read |
| Snippet/JSON size limits enforced at IPC | `CLAUDE.md` "Security" | ✅ New `.sqlpromptstyle` import capped at 1 MB |
| Never commit / push without explicit user approval | `CLAUDE.md` (global) | ✅ Plan generation creates files only |

**Verdict**: no gate violations. **Complexity Tracking** section below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/020-sqlprompt-visual-parity/
├── plan.md              # This file (/speckit.plan output)
├── spec.md              # Feature specification (already written)
├── research.md          # Phase 0 — decisions, rationale, alternatives
├── data-model.md        # Phase 1 — entities, fields, relationships, state
├── quickstart.md        # Phase 1 — how a dev picks up the work
├── contracts/           # Phase 1 — IPC contract additions / extensions
│   ├── ipc-style-editor-schema.md     # RequestStyleEditorSchema (28 / 128)
│   ├── ipc-profile-import-sqlprompt.md # ProfileImport extension for .sqlpromptstyle
│   └── ipc-format-preview-debounce.md  # FormatPreview usage pattern for live preview
├── checklists/
│   └── requirements.md  # already created by /speckit.specify
└── tasks.md             # Phase 2 — generated by /speckit.tasks (NOT this command)
```

### Source Code (repository root)

This feature touches the following existing project trees and adds the following new files / folders.

```text
src/AkmlSql.Shell.Shared/                       # Shared project — all 6 shell extensions consume this
├── Ui/Theme/
│   ├── ThemeTokens.cs                          # EXTEND: add SQL Prompt parity tokens
│   │                                           #   - IconBadge.Table, IconBadge.View, IconBadge.Column,
│   │                                           #     IconBadge.StoredProc, IconBadge.Function, IconBadge.Snippet,
│   │                                           #     IconBadge.Keyword, IconBadge.Database, IconBadge.Schema,
│   │                                           #     IconBadge.Trigger, IconBadge.Index, IconBadge.Synonym
│   │                                           #   - TabColor.* swatches (8 default swatches per SQL Prompt)
│   │                                           #   - History.OpenIcon, History.ClosedIcon, History.Star.Active,
│   │                                           #     History.Star.Inactive, History.MatchHighlight
│   │                                           #   - Spacing.* and Typography.* token families
│   ├── ThemeRegistry.cs                        # EXTEND: bind the new tokens to Light + Dark hex values
│   │                                           #   sourced from doc/SQL-PROMPT/ tables
│   ├── HostThemeWatcher.cs                     # UNCHANGED — already handles all 6 hosts
│   └── ThemeManager.cs                         # DELETE remaining members once T044 finishes
├── Formatting/
│   ├── FormatStylesEditorWindow.xaml.cs        # NEW: three-panel WPF window (style list /
│   │                                           #   settings tree / settings + live preview)
│   ├── FormatStylesEditorViewModel.cs          # NEW: drives the editor; handles import / export / preview
│   ├── StyleListPanel.xaml.cs                  # NEW: left panel — Your Styles + Redgate-imported styles
│   ├── SettingTreePanel.xaml.cs                # NEW: middle panel — tree built from FormatSettingSchema
│   ├── SettingControlsPanel.xaml.cs            # NEW: right panel top — type-driven controls
│   ├── LivePreviewPanel.xaml.cs                # NEW: right panel bottom — debounced FormatPreview consumer
│   └── FormatRequestDispatcher.cs              # EXISTS (untracked on 019 branch); extend with preview debounce
├── Options/                                    # EXTEND: complete Options-dialog parity
│   └── (existing OptionsDialog files)          #   - reuse three-pane layout; add missing pages from SQL Prompt
└── Help/                                       # UNCHANGED for this feature

src/AkmlSql.Engine/
├── Formatter/
│   ├── FormatterPipeline.cs                    # UNCHANGED (7-stage; we only feed it more profile values)
│   ├── Profiles/
│   │   ├── FormatProfile.cs                    # EXTEND: add fields for every SQL Prompt setting,
│   │   │                                       #   plus a Dictionary<string, JsonElement> for pass-through
│   │   │                                       #   round-trip preservation
│   │   ├── SqlPromptStyleImporter.cs           # NEW: .sqlpromptstyle JSON → FormatProfile
│   │   ├── SqlPromptStyleExporter.cs           # NEW: FormatProfile → .sqlpromptstyle JSON (round-trip safe)
│   │   ├── FormatSettingSchema.cs              # NEW: canonical descriptor consumed by the editor UI
│   │   │                                       #   (group / id / name / type / default / range / sqlPromptKey)
│   │   └── SqlPromptKeyMap.cs                  # NEW: 1:1 / 1:many mapping table — SQL Prompt JSON path
│   │                                           #   to FormatProfile field
│   └── Profiles/Tests/                         # NEW: xunit suite for importer, exporter, schema
└── Server/
    └── FormatRequestHandler.cs                 # EXTEND: handle ProfileImport when extension is .sqlpromptstyle;
                                                #   add RequestStyleEditorSchema handler

src/AkmlSql.Core/
├── Config/AppSettings.cs                       # EXTEND: bump FormatterSettings to expose imported-style folder
└── Ipc/RpcMessage.cs                           # EXTEND: add MessageTypes.RequestStyleEditorSchema = 28
                                                #   and .StyleEditorSchemaResult = 128

tests/
├── AkmlSql.Core.Tests/
│   ├── Format/
│   │   ├── SqlPromptStyleImporterTests.cs      # NEW: validate every SQL Prompt key parses correctly
│   │   ├── SqlPromptStyleExporterTests.cs      # NEW: round-trip every profile through export → import
│   │   └── SqlPromptKeyMapTests.cs             # NEW: every mapped key has a defined default in AKML
│   └── Theme/
│       └── HardcodedHexScannerTests.cs         # NEW: SC-001 enforcement — fail build on any non-semantic
│                                               #   hex in src/AkmlSql.Shell.Shared/**/*.{cs,xaml}
└── format-parity/                              # NEW: parity corpus + golden outputs
    ├── README.md
    ├── corpus/                                  # 200 representative .sql inputs
    ├── styles/                                  # 20 representative .sqlpromptstyle files
    └── golden/                                  # SQL Prompt's actual output, one file per (corpus, style) pair

doc/SQL-PROMPT/                                  # UNCHANGED — canonical visual contract; we read, never write
```

**Structure Decision**: Multi-host IDE extension with shared source via `.projitems`. We follow the existing layout (`src/AkmlSql.{Shell.Shared,Engine,Core}`) and add only the new files listed above. No new top-level project. No new directory hierarchy except `tests/format-parity/`. The choice is forced by the codebase: shells must remain `.NET Framework 4.7.2` and share via `AkmlSql.Shell.Shared.projitems`, and the engine must remain `.NET 10` with the formatter pipeline already in place.

## Phase 0 — Outline & Research

### Unknowns / decisions surfaced from the spec + Technical Context

| ID | Question | Why it matters | Status |
|---|---|---|---|
| R1 | Are the SQL Prompt token mappings in `doc/SQL-PROMPT/` already present in `ThemeTokens`, or do we need new tokens? | Determines whether FR-001 / FR-002 is mostly migration or mostly addition. | Resolved in research.md |
| R2 | What is the exact mapping from `.sqlpromptstyle` JSON keys to AKML `FormatProfile` fields? | Foundation for FR-019 / FR-020 / FR-028. | Resolved in research.md (table) |
| R3 | Can `FormatPreview` IPC round-trip meet the 250 ms target for a 200-line sample? | SC-009 viability. | Resolved in research.md (benchmark plan) |
| R4 | Should the Format Styles editor live in a new window or inside the Options dialog? | UI parity vs single-modal architecture. | Resolved in research.md |
| R5 | Which SQL Prompt format settings are already implemented in the AKML formatter, which are gaps? | Bounds the formatter-side work for US5. | Resolved in research.md (gap matrix) |
| R6 | How should existing user customisations be preserved on first launch with the new tokens? | FR-030, SC-011 risk. | Resolved in research.md |
| R7 | Does the WPF surface set scale correctly at 200 % DPI today? | SC-005 viability. | Resolved in research.md |
| R8 | What is the round-trip strategy for unknown `.sqlpromptstyle` keys? | FR-024 — files re-exported must not lose data. | Resolved in research.md |

### Research consolidation

The detailed Decision / Rationale / Alternatives entries live in [`research.md`](./research.md). Summary of decisions:

- **R1**: Tokens for chrome (Surface / Text / Border / Accent / Status / Editor / Chat) already exist (spec 016). New tokens needed: 12 `IconBadge.*` per object type, 8 `TabColor.*` swatches, 5 `History.*` semantic markers, 4 `Spacing.*` scalars, 4 `Typography.*` font tokens. Add to `ThemeTokens.cs` + `ThemeRegistry.cs`.
- **R2**: 50 SQL Prompt JSON keys → AKML fields. ~ 30 have direct equivalents; ~ 15 need transformation (enum names differ); ~ 5 are not yet supported (e.g. `useObjectDefinitionCase`) and become "Settings not yet supported" surface entries.
- **R3**: 250 ms feasible. `FormatPreview` round-trip measured ~ 60 ms for 200-line sample in dev. Add 100 ms debounce on UI side, room to spare.
- **R4**: New modal window (`FormatStylesEditorWindow`). Matches SQL Prompt UX (Options dialog opens the style editor as a separate window). Lets the live preview pane take real estate.
- **R5**: AKML formatter pipeline covers ~ 30 / 50 SQL Prompt settings today. Gaps: collapse thresholds per category, JOIN keyword alignment variants, CASE WHEN alignment variants, IN statement alignment, CTE column placement enum, operator alignment, function-call layout. Each becomes a discrete formatter-pipeline task in `tasks.md`.
- **R6**: First-launch migration writes a `themeMigration.v1.json` flag in `%AppData%/AKML SQL/`. If user has any `legacyColorOverrides` in config, they take precedence over the new tokens and a one-time notice is queued for next dialog open.
- **R7**: WPF is DIU-based; existing surfaces inherit. Audit hardcoded `Width` / `Height` in XAML and `.cs`; replace any pixel-tuned values with DIU-correct ones. Hardcoded-hex scanner extended to also flag `Width="123.4"`-style absolute literals outside permitted lists.
- **R8**: `FormatProfile` gains a `Dictionary<string, JsonElement> _passthrough` populated by the importer with any unknown JSON keys; exporter writes them back verbatim at the same JSON paths.

**Output**: [`research.md`](./research.md) — one section per R-ID with Decision / Rationale / Alternatives considered.

## Phase 1 — Design & Contracts

### Data model — entities & relationships

Detailed in [`data-model.md`](./data-model.md). High-level entity map:

```text
ThemeToken (name, category, lightValue, darkValue, type)
  ├── consumed by → Surface (name, requiredTokens[], referenceDocPath)
  └── grouped by → TokenCategory (enum)

ThemeVariant (enum: Light, Dark) — selected by HostThemeWatcher per host

FormatStyle (id, name, kind: Native|SqlPromptImported, settingValues, passthroughUnknownKeys)
  ├── has-many → FormatSetting (id, group, name, type, default, range, sqlPromptKey)
  ├── persisted-as → StyleFile (path, schemaVersion, parsedJson)
  └── related-to → SqlPromptStyleMapping (sqlPromptJsonPath → AkmlSettingId, transform)

VisualReference (surfaceName, docPath, colorTable[], dimensionTable[], svgMockup)
  └── validated-against → Surface
```

**State transitions**:

- `FormatStyle.kind` is fixed at creation: a style is either Native (created in AKML's editor) or SqlPromptImported (loaded from `.sqlpromptstyle`). Both can be re-exported as `.sqlpromptstyle`.
- A `FormatStyle` can be active (only one at a time per `FormatterSettings.ActiveProfile`) or inactive.
- A `FormatStyle` flagged as `IsReadOnly` (built-in Redgate-style defaults) cannot be edited; editing forks a Native copy.

### Interface contracts

Three IPC contract documents in [`contracts/`](./contracts/). Summary:

| Contract | Direction | New / Extended | Purpose |
|---|---|---|---|
| `ipc-style-editor-schema.md` | Shell → Engine `RequestStyleEditorSchema (28)` ↔ Engine → Shell `StyleEditorSchemaResult (128)` | **NEW** | Editor UI requests the canonical `FormatSettingSchema` (groups + settings + types + defaults + ranges). Lets the editor build its tree from one source of truth instead of duplicating the schema. |
| `ipc-profile-import-sqlprompt.md` | Shell → Engine `ProfileImport (17)` ↔ Engine → Shell `ProfileImportResult (117)` | **EXTENDED** | Importer detects `.sqlpromptstyle` extension and routes through `SqlPromptStyleImporter`. Result envelope adds `unsupportedSettings: string[]` and `passthroughKeys: string[]`. |
| `ipc-format-preview-debounce.md` | Shell → Engine `FormatPreview (12)` ↔ Engine → Shell `FormatPreviewResult (112)` | **EXTENDED (usage)** | Documents the 100 ms debounce, cancellation semantics (later request supersedes earlier), and the standard 200-line sample SQL the editor ships with. No payload change. |

### Quickstart

[`quickstart.md`](./quickstart.md) walks a developer through:

1. Clone, branch checkout, MSBuild build of `AkmlSql.Ssms22` (fastest feedback loop).
2. Publish engine: `dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64`.
3. Install in SSMS 22 (extension path under `Release/`).
4. Open Options → Format → Styles → "Import…" — pick a `.sqlpromptstyle` from `tests/format-parity/styles/`.
5. Verify imported style appears, set as active, format a sample document, compare to golden output in `tests/format-parity/golden/`.
6. Open the in-editor suggestion popup and verify icon badges read from `ThemeTokens.IconBadge.*` (toggle Light / Dark in SSMS theme settings; surface re-themes < 1 s).
7. Run `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj` to confirm importer / exporter / scanner suites pass.

### Agent context update

This repo uses Claude Code as the agent. Per `update-agent-context.ps1 -AgentType claude`, the agent context lives in `CLAUDE.md`. The script is auto-run after Phase 1; if it fails to detect new technology, the manual additions block of `CLAUDE.md` already carries the relevant conventions (WPF theming, frozen brushes, hoisted fonts, DTE-owner pattern). No new technology is introduced by this plan — every dependency (WPF, MessagePack, Serilog, ScriptDom, VSSDK) is already in the project. The script run is therefore a no-op other than refreshing the "Active branch" line.

### Re-evaluation of Constitution Check (post-design)

| Gate | Re-check | Result |
|---|---|---|
| No hardcoded chrome hex | Phase 1 design uses tokens exclusively for new surfaces | ✅ |
| Brushes frozen / fonts hoisted | New WPF surfaces follow the pattern | ✅ |
| Modal owner via DTE HWND | `FormatStylesEditorWindow` follows the `HistoryDiffWindow` reference | ✅ |
| Out-of-process engine boundary | All formatting / preview / import still runs in the engine | ✅ |
| Atomic config writes | Style files use temp-file + rename | ✅ |
| Path canonicalisation | Import path passes through `Path.GetFullPath` | ✅ |
| IPC size limits | `.sqlpromptstyle` capped at 1 MB | ✅ |
| Build via individual MSBuild | No change | ✅ |
| No unauthorised git ops | Plan generation creates files only | ✅ |

**Verdict**: still no violations. Complexity Tracking remains empty.

## Complexity Tracking

> Fill ONLY if Constitution Check has violations that must be justified.

*No violations. Section intentionally empty.*

---

## Notes for `/speckit.tasks`

When `/speckit.tasks` runs next, it should generate one task per discrete unit, grouped by priority and dependent on the spec's user stories. Anchor points:

- **P1 tasks**: token additions in `ThemeTokens.cs`, hex bindings in `ThemeRegistry.cs`, hardcoded-hex scanner test, theme migration flag handler, `SqlPromptStyleImporter` + tests, `SqlPromptStyleExporter` + round-trip tests, `SqlPromptKeyMap` + coverage tests, `FormatProfile` field additions and pass-through dictionary, extension of `ProfileImport` IPC handler.
- **P2 tasks**: `FormatStylesEditorWindow` shell + three panels, `FormatSettingSchema` + `RequestStyleEditorSchema` IPC, live preview debounce, every gap-rule task in the formatter pipeline (R5), Options dialog page completion, suggestion popup re-skin pass, object-definition box re-skin, column picker re-skin, snippet manager re-skin.
- **P3 tasks**: SQL History window colour audit, **Tab Coloring** — *swatch palette alignment only* (Q3) plus the FR-011a audit doc; Phase 5's assignment-rule engine remains untouched. Code Analysis severity palette, AI window chrome audit, ghost-text foreground audit, tooltip chrome audit, editor-margin spinner audit.
- **Built-in styles (FR-027a)**: a P1 task in US2 transcribes 3 read-only Native styles (Compact, Indented, AlignedLeftBracket) and seeds them via `BuiltInStyleSeeder` at engine startup. AKML does NOT redistribute Redgate-authored `.sqlpromptstyle` files.
- **Active-style scope (FR-027b)**: implementation MUST keep a single global `ActiveProfile`; tasks include a regression test (`ActiveProfileScopeTests`) verifying scope cannot drift per-host.
- **Unsupported settings UX (FR-023)**: the `SettingTreePanel` renders unsupported entries with `IsEnabled=false`, the imported value shown, and an "Unsupported" badge user control next to the control — no separate bottom panel.
- **Cross-cutting**: DPI audit (per host × 100/125/150/200%), colour-blind simulation pass (FR-029 / SC-012), screenshot-comparison reviewer protocol (SC-003 / SC-004), parity-corpus assembly (SC-007).
