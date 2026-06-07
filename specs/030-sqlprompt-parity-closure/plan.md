# Implementation Plan: SQL Prompt Parity Gap Closure (excluding AI & licensing)

**Branch**: `030-sqlprompt-parity-closure` | **Date**: 2026-06-07 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/030-sqlprompt-parity-closure/spec.md`

> **GIT RULE (project-wide, overrides any sub-skill's "commit often"):** This repo forbids `git add/commit/push` without the user's explicit "yes" to "Ready to commit?". Any `Commit` checkpoint in downstream tasks is **summarize-and-ask**, not an automatic git action.

## Summary

Close the in-scope 🟡/❌ rows of the `doc/_Prompt-Gap/` SQL Prompt 11 audit across IntelliSense, Formatting, Refactoring, Code Analysis, Snippets, Tabs/History, Options, and Platform. The audit's headline finding — **"built but not wired"** — drives the approach: the majority of gaps are existing, unit-tested code that the running product never reaches, so most work is **finishing and connecting** rather than greenfield. Delivered as **one feature, phased by priority** (P1 → P2 → P3) per the clarifications.

Technical approach by leverage:

- **Wire dormant code into live paths** (genuinely low-cost connect-existing work): dispatch the standalone format actions in `FormatRequestHandler.HandleFormatAction`; thread the document directory into the live `AnalysisEngine` so `.casettings`/suppressions apply in the editor; connect the `QuickInfoSource`/`SignatureHelpSource` shell stubs to the existing engine handlers; call `TempTableTracker` from `CompletionEngine`; make the suggestion on/off/auto-trigger/scope settings gate behavior; fix the snippet commit path so desktop expansion works.
- **Activate the formatter layout rules — feasible but risk-gated** (P1, but *not* cheap): the `Rules/*` passes exist on the real `List<LayoutNode>` IR and even include CASE/CTE recognition, but they were only tested in isolation, and the CTE/CASE/Operators/IN-list layout was previously **deferred as architectural** (progress.md 2026-05-23). P1 therefore **starts with a de-risk spike** that proves idempotency (Stage 7) + semantic validation (Stage 6) hold through the full pipeline, then does a graduated per-rule-group rollout; Operators/IN-list may slip to a follow-up. See research **R1**.
- **Finish partially-built UI**: style create/copy/set-active buttons, column picker, Manage-Rules dialog, Options controls for config-only settings, Command-Palette object search, the Bulk-Format wizard launcher.
- **New build where nothing exists**: database-wide Smart Rename (dependency-aware, reviewable script — per clarification), Find Invalid Objects, Inline proc / Inline EXEC, INSERT→UPDATE, Script-as-ALTER, `.sqlpromptsnippet` import, built-in snippet pack, tab-coloring-by-database, version-preserving history retention.

A non-regression performance gate (SC-011) guards the hot paths, since activating dormant rules and live analysis runs more work on every format/keystroke.

## Technical Context

**Language/Version**: C# (LangVersion latest). Shell shared sources (`AkmlSql.Shell.Shared` `.projitems`) compile to **net472** under each host (SSMS 22 / VS 2026, VS SDK 17.14.x, x64). `AkmlSql.Core` dual-targets **netstandard2.0 + net10.0**. `AkmlSql.Engine` and the extracted libraries (`AkmlSql.Formatting`, `AkmlSql.IntelliSense`, `AkmlSql.Analysis`) target **net10.0**; the engine ships self-contained, single-file, win-x64, trimmed.  
**Primary Dependencies**: VS SDK 17.14 (shell), `Microsoft.SqlServer.TransactSql.ScriptDom` (`TSql170Parser`), MessagePack (IPC), Serilog, System.Text.Json, WPF (programmatic, no XAML), `Microsoft.Data.SqlClient` (engine) / `System.Data.SqlClient` (shell).  
**Storage**: `%AppData%/AKML SQL/config.json` (AppSettings); `.akmlstyle` formatting profiles; `.akmlsnippet` snippets; `.casettings` per-project rule settings; SQLite + FTS5 for SQL History; preview-sample + result files.  
**Testing**: xunit (net10 test projects). Engine/library logic is TDD-first (`tests/AkmlSql.{Core,Engine,Formatting,IntelliSense,Analysis}.Tests`). Shell `.projitems` sources are compiled and unit-tested via `tests/AkmlSql.Shell.Shared.Tests`. UI-bound paths (DTE/pipe/margin) are verified live.  
**Target Platform**: Windows desktop — SSMS 22 (x64) and Visual Studio 2026 (x64); engine win-x64. **This is the parity bar**; the Blazor Web edition is out of scope here.  
**Project Type**: Desktop IDE extension (in-proc shell) + out-of-process .NET 10 engine, over an ACL'd named pipe (MessagePack frames).  
**Performance Goals**: Code completion p95 < 100 ms; Format SQL < 200 ms on typical scripts (≤ ~500 lines); large scripts never block the IDE UI (SC-011).  
**Constraints**: Preserve the IPC **wire format** (existing message codes unchanged; new codes only from reserved free ranges). Engine stays trimmable (no reflection-dependent additions without a `TypeInfoResolver`). Shell built with **full MSBuild per host** (never `dotnet build`, never via the solution — VSCT cross-contamination). WPF chrome uses `ThemeManager`/`ThemeRegistry` tokens + frozen brushes (no hardcoded hex). Atomic config writes. Formatting must keep passing **Stage 6 (semantic validation)** and **Stage 7 (idempotency)** after new rule passes.  
**Scale/Scope**: 8 prioritized user stories, 49 functional requirements, ~120 analysis rules, the existing formatter/snippet/analysis/history/options subsystems. No new external services (AI excluded).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No `.specify/memory/constitution.md` exists. The de-facto project principles in `CLAUDE.md` are used as gates:

| Gate (from CLAUDE.md) | Assessment |
|---|---|
| **Out-of-process boundary** — engine logic stays in the engine/libraries; shell only orchestrates UI + IPC | ✅ All analysis/format/refactor/completion logic stays engine-side; shell changes are UI wiring + sending existing/new IPC. |
| **IPC wire compatibility** — existing message codes unchanged; new codes from reserved ranges | ✅ Most work reuses existing messages (Format*, RequestAnalyze, RequestQuickInfo, RequestSignatureHelp, RefactorPreview/Apply, Snippet*). A few new codes (Find Invalid Objects, list-rules, object-search) allocate from the free 27/127, 29/129, 94–99/194–199 ranges. |
| **Shared `.projitems`, built per host with full MSBuild** | ✅ No change to the build model; shell edits land in shared sources compiled against each host SDK. |
| **TDD for engine/Core logic** | ✅ Plan sequences failing-test-first for every engine/library change; UI-only wiring verified live. |
| **WPF theme tokens + frozen brushes (no hardcoded hex)** | ✅ New dialogs (column picker, Manage Rules, Options controls) follow the `ThemeRegistry`/`SafetyWarningDialog` patterns. |
| **Atomic config writes** | ✅ New persisted settings reuse `ConfigManager`'s temp+rename path. |
| **Formatting semantic-equivalence + idempotency (Stages 6/7)** | ✅ The rule-wiring decision (research.md R1) keeps both gates; rules must be idempotent and validation-preserving (test gate). |
| **Performance non-regression (SC-011)** | ✅ Benchmarks added on the hot paths (completion, format) before/after wiring. |

**Result: PASS** — no violations; Complexity Tracking left empty.

## Project Structure

### Documentation (this feature)

```text
specs/030-sqlprompt-parity-closure/
├── plan.md              # This file
├── spec.md              # Feature spec (clarified)
├── research.md          # Phase 0 — technical decisions per gap area
├── data-model.md        # Phase 1 — entities + code mapping + new fields/state
├── quickstart.md        # Phase 1 — build/test/validate workflow
├── contracts/
│   └── ipc-and-commands.md   # Phase 1 — IPC message + command-surface deltas
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit.specify)
└── tasks.md             # Phase 2 — created by /speckit.tasks (NOT here)
```

### Source Code (repository root — directories this feature touches)

```text
src/
├── AkmlSql.Formatting/              # P1 — wire Rules/* into the pipeline
│   ├── Pipeline/FormatterPipeline.cs        # insert rules pass after BuildLayout
│   ├── Rules/{Dml,Ddl,Join,List,Parenthesis,ControlFlow}Rules.cs  # already implemented; now invoked
│   ├── Layout/{AlignmentCalculator,CollapseEvaluator}.cs          # already implemented; now invoked
│   └── Actions/*Action.cs                    # already implemented; now dispatched
├── AkmlSql.Engine/
│   ├── Formatter/FormatRequestHandler.cs     # dispatch action types 0–5; run format-time actions
│   ├── Handlers/{Completion,Analysis,Refactoring,Snippets,Schema}/  # wire QuickInfo/SigHelp/temp-table/.casettings; new handlers
│   ├── Refactoring/                          # new: SmartRename(DB-wide), FindInvalidObjects, InlineProc, InlineExec, InsertToUpdate
│   ├── Snippets/                             # snippet expand fix, .sqlpromptsnippet import, built-in pack
│   └── History/                              # version-preserving retention, remove-older-than
├── AkmlSql.IntelliSense/Completion/          # honor Enabled/AutoTrigger/ColumnScope; temp-table; alias policy; scope
├── AkmlSql.Analysis/                         # CaSettingsLoader live-dir threading; rule listing for Manage-Rules
├── AkmlSql.Core/
│   ├── Config/AppSettings.cs                 # new settings (where missing) for Options coverage
│   └── Ipc/                                  # new message types + message classes
└── AkmlSql.Shell.Shared/                     # built per host (SSMS22 + VS2026)
    ├── Editor/{QuickInfoSource,SignatureHelpSource}.cs   # connect stubs to engine
    ├── Editor/Completion/CompletionController.cs          # snippet commit fix; column picker; settings gating
    ├── Formatting/                           # style create/copy/set-active; error popup; current-query preview
    ├── Analysis/                             # Manage-Rules dialog; lightbulb severity; issue-details popup; analysis toggle
    ├── Refactoring/                          # commands for the new refactors
    ├── Snippets/                             # create-from-selection; surround-with; variable authoring
    ├── Tabs/                                 # tab-color-by-database
    ├── Productivity/                         # Command-Palette object search; Bulk-Format launcher
    └── Dialogs/Pages/                        # Options controls for config-only settings

tests/
├── AkmlSql.Formatting.Tests/   AkmlSql.Engine.Tests/   AkmlSql.IntelliSense.Tests/
├── AkmlSql.Analysis.Tests/     AkmlSql.Core.Tests/     AkmlSql.Shell.Shared.Tests/
```

**Structure Decision**: No new projects. The feature edits existing projects in place, respecting the out-of-process boundary (engine/library logic engine-side; UI in the shared shell). New message types and config fields extend `AkmlSql.Core`. The phased delivery maps to priority: **P1** = `AkmlSql.Formatting` + `FormatRequestHandler` + shell formatting UI, **beginning with the R1 de-risk spike + a perf baseline** (research R1 / cross-cutting), then the graduated rule-group rollout and the cheap action-dispatch wiring; **P2** = `AkmlSql.IntelliSense`/completion shell + snippets; **P3** = analysis-config, refactoring, tabs/history, options, platform.

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
