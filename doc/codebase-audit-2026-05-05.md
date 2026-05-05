# AKML-SQL Codebase Audit — 2026-05-05

Branch: `016-wpf-theme-refresh` @ `7b8bd53`
Scope: 681 .cs files, ~95K LOC, plus VSCT / Inno Setup / PowerShell

---

## Executive summary

The codebase is in good shape relative to its size. There is **no significant dead code**, **no naming inconsistency at the public API level**, and the per-shell-target duplication that *looks* problematic is the deliberate multi-SDK strategy documented in CLAUDE.md.

The real opportunities:

1. **PipeRpcServer.cs has a 55-case switch dispatching to 18+ handlers** — replace with a `Dictionary<int, IMessageHandler>` dispatch table. Highest ROI of anything in this report.
2. **Three files exceed 1,500 lines** (SettingsWindow 3196, HistoryToolWindowControl 2201, AiRequestHandler 1892, CompletionController 1466). Each has clear seams; none is irreducibly complex.
3. **40+ command files share an identical "double-Initialize-with-AsyncPackage-and-Package" pattern** that can be extracted to a single helper.
4. **Three WPF surfaces still allocate `FontFamily` per-call** in violation of CLAUDE.md WPF guidance.
5. **14 actionable TODOs**, mostly clustered around incomplete IPC wiring (signature help, quick info, format-on-event triggers).

There are no HACK or FIXME markers in the codebase — every flag is a TODO.

---

## 1. TODO action plan (prioritized)

The codebase has **14 real TODOs** (no HACK, no FIXME). Items below are ordered by user-visible impact.

### P0 — Visible feature gap

| # | File:line | Description | Effort |
|---|---|---|---|
| 1 | `src/AkmlSql.Shell.Shared/Editor/SignatureHelpSource.cs:51` | Skeleton — `SignatureRequest` IPC never sent, so signature help is silently dead in the editor. Class header at line 28 already calls itself a "skeleton". | M |
| 2 | `src/AkmlSql.Shell.Shared/Editor/QuickInfoSource.cs:73` | Skeleton — `QuickInfoRequest` IPC never sent. Same pattern as above. | M |
| 3 | `src/AkmlSql.Shell.Shared/Editor/SignatureHelpSource.cs:66` | "Implement best match selection based on active parameter" — depends on #1 landing first. | S |

**Action:** Either wire these up properly via `PipeRpcClient` (matching the patterns in `CompletionController` for `MessageTypes.Completion*`), or **delete the skeleton classes and their MEF exports** so users don't see a registered-but-broken provider. Half-implemented features are worse than missing ones — pick one direction this sprint.

### P1 — Quality-of-life integrations

| # | File:line | Description | Effort |
|---|---|---|---|
| 4 | `src/AkmlSql.Shell.Shared/Formatting/FormatOnSaveHandler.cs:47` | "Wire to formatter pipeline via engine IPC when available" — currently a no-op. | M |
| 5 | `src/AkmlSql.Shell.Shared/Formatting/FormatOnPasteHandler.cs:50` | Same (paste). | M |
| 6 | `src/AkmlSql.Shell.Shared/Formatting/FormatOnDelimiterHandler.cs:62` | Same (semicolon/closing-paren delimiter). | M |
| 7 | `src/AkmlSql.Shell.Shared/Productivity/CrudGenerationCommand.cs:71` | "Show a dialog to collect schema name, table name, and operation options." Currently uses word-at-caret heuristic — works but feels half-finished. | S |

**Action for #4–6:** All three handlers send the same "format the buffer" request — extract to one shared helper (`FormatRequestDispatcher`) and have each handler hook a different event. Nine instances of the same TODO in three files.

### P2 — SSMS host-specific polish

| # | File:line | Description | Effort |
|---|---|---|---|
| 8 | `src/AkmlSql.Shell.Shared/Tabs/TabTooltipProvider.cs:129` | "SSMS-specific connection context retrieval" — falls back to caption parsing today, which works but tooltip lacks auth-mode/connect-time. | M |
| 9 | `src/AkmlSql.Shell.Shared/Tabs/TabTooltipProvider.cs:158` | "Walk the WPF visual tree to find the tab header" — for richer hover positioning. | L |
| 10 | `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs:896, 904` | "Approaches 2 and 3 for connection context" — same problem class as #8. | M |
| 11 | `src/AkmlSql.Shell.Shared/Productivity/Grid/GridAccessHelper.cs:18` | "SSMS 20 uses a different results pane class than 21/22" — version-specific fallback paths. | S |

**Action:** #8, #10 are the same problem in two places. Extract a single `SsmsConnectionContextResolver` that both can call; today they each have a copy of the comment but no implementation. #11 is small but blocks SSMS 20 grid features.

### P3 — Cosmetic / placeholder values

| # | File:line | Description | Effort |
|---|---|---|---|
| 12 | `src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs:66` | `WasFormatted = false // TODO: integrate format-on-expand` — DTO field always false. | S |
| 13 | `src/AkmlSql.Engine/Snippets/SnippetRequestHandler.cs:95` | `UsageCount = 0 // TODO: integrate usage tracker` — same, always 0. | S |
| 14 | `src/AkmlSql.Installer/AkmlSqlSetup.iss:42` | "T096: On uninstall, restore native SSMS IntelliSense if AKML SQL disabled it." | S |

**Action:** #12–13 are field DTOs that have hardcoded values. If the consuming UI doesn't display them, **delete the fields** — keeping `UsageCount = 0` in the wire format is misleading. #14 is a small Inno Setup pascal script change.

> **Note:** `GridScriptGenerator.cs:89, 117, 182` contain three `-- TODO: Replace [TableName]` strings, but those are **inside generated SQL output** (instructions to the user reading the generated script), not code TODOs. They are intentional.

---

## 2. Dead code

**None of consequence.** The agent verified that all "looks unused" candidates are actually MEF-discovered (`[Export(typeof(IWpfTextViewCreationListener))]`), command-routing targets (`[Guid]`+`CommandId`), or DTE-callback wired.

The only dead class previously found in this branch — `SchemaStatusIndicator.cs` — was removed in commit `7b8bd53`. None remain.

---

## 3. Duplication that should be extracted

### 3.1 Command Initialize double-overload pattern (40+ files)

Most files in `src/AkmlSql.Shell.Shared/Commands/` follow this exact shape:

```csharp
public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
    => Instance = new XxxCommand(package, commandService);

public static void Initialize(Package package, OleMenuCommandService commandService)
    => Instance = new XxxCommand(package, commandService);
```

Affected (verified): `AiExplainCommand.cs:49-57`, `AiFixCommand.cs`, `AiOptimizeCommand.cs`, `AiIndexAnalysisCommand.cs`, `AiChatPanelCommand.cs`, plus the rest of the `Commands/` directory.

**Why both overloads exist:** SSMS 20 / VS 2019 use VS SDK 15.x/16.x where `AsyncPackage` and `Package` lookups differ; SSMS 21+/VS 2022+ use SDK 17.x. Each shell's `AkmlSqlPackage.cs` calls one or the other.

**Extraction:** Single static helper `CommandFactory.RegisterMenu(object package, OleMenuCommandService svc, CommandID id, EventHandler executeHandler, EventHandler? beforeQueryStatus = null)` — handles both `AsyncPackage` and `Package` via the common `Package` base type, builds the `OleMenuCommand`, registers it.

Each command file shrinks from ~70 lines to ~25. Effort: medium (mostly mechanical), reach: high. **Confidence: high** — this exact pattern repeats across 40+ files with zero variation.

### 3.2 FontFamily per-call allocation (3 files)

CLAUDE.md WPF section explicitly requires `FontFamily` to be hoisted to `static readonly`. Three places still allocate per-call:

| File | Issue |
|---|---|
| `src/AkmlSql.Shell.Shared/Productivity/Navigation/ReferencesPanel.cs:78` | `FontFamily = new FontFamily("Segoe UI")` per element creation |
| `src/AkmlSql.Shell.Shared/Snippets/SnippetManagerDialog.cs` (Consolas TextBox) | Per-instantiation Consolas allocation |
| `src/AkmlSql.Shell.Shared/Ui/SqlPreviewRenderer.cs` | Consolas allocation per render |

**Fix (each):** add `private static readonly FontFamily UiFont = new FontFamily("Segoe UI");` (or `ConsolasFont`) at class scope, reuse. Effort: trivial. **Confidence: high.**

### 3.3 "Format file via DTE" duplication

`FormatOnSaveHandler`, `FormatOnPasteHandler`, `FormatOnDelimiterHandler` all have the same "send format request" TODO comment at the same shape. When you wire them up (P1 items #4–6 above), put the IPC dispatch in one place — don't paste it three times.

---

## 4. Code-smell hotspots

Already-clean items I verified and rejected: ThemeBrushSet.Freeze() usage (correct, mandated), `using System.Windows.Forms` in Ai panels (used, not unused), nested config classes (mostly justified — see §6).

The only real smell beyond the FontFamily issue (3.2): no large classes hold mutable static state inappropriately, no singletons that should be DI'd at runtime, no obvious O(n²) loops. The codebase keeps its sharp edges in known places.

---

## 5. Architecture / refactoring opportunities

Ranked by ROI.

### 5.1 ★ PipeRpcServer dispatch table — HIGHEST ROI

**Problem:** `src/AkmlSql.Engine/Server/PipeRpcServer.cs` is 937 lines. The bulk (~520 lines, lines 160-683) is a single `switch` with 55 cases dispatching to 18+ handler classes that are all stored as fields (lines 37-59).

**Refactor:**

```csharp
// new file: AkmlSql.Engine/Server/IMessageHandler.cs
internal interface IMessageHandler {
    Task<RpcMessage?> HandleAsync(RpcMessage message);
}

// in PipeRpcServer ctor:
_handlers = new Dictionary<int, IMessageHandler> {
    [MessageTypes.CompletionRequest]      = new CompletionHandler(_completionEngine),
    [MessageTypes.SchemaStatusRequest]    = new SchemaStatusHandler(_schemaCacheManager),
    [MessageTypes.SchemaRefreshRequest]   = new SchemaRefreshHandler(/*...*/),
    // ...30 more
};

// in dispatch:
return _handlers.TryGetValue(message.MessageType, out var h)
    ? await h.HandleAsync(message)
    : CreateErrorResponse($"Unknown type {message.MessageType}", message.RequestId);
```

**Effect:** PipeRpcServer drops to ~250 lines (frame loop + dispatch + handler registration). Adding a new message type requires zero changes to the server class. Tests can swap individual handlers.

**Effort:** Medium. Each existing case is already calling a handler method — wrapping them in `IMessageHandler` is mechanical. **Confidence: high.** This is the single biggest win in the report.

### 5.2 ★ AppSettings.cs split (961 lines, 19 nested classes)

`src/AkmlSql.Core/Config/AppSettings.cs` defines 19 nested config classes (`IntelliSenseSettings`, `CacheSettings`, `FormatterSettings`, `SnippetSettings`, `CodeAnalysisSettings`, `RefactoringSettings`, `HistorySettings`, `TabSettings`, `SafetySettings`, `GridSettings`, `EditorProductivitySettings`, `ExecutionProductivitySettings`, `NavigationSettings`, `CommandPaletteSettings`, `AiSettings`, `CompletionPolishSettings`, …) all in one file.

**Refactor:** split into `Config/IntelliSenseSettings.cs`, `Config/FormatterSettings.cs`, etc. (nine to ten files, each 80-150 lines). Keep `AppSettings.cs` as the thin root aggregate. JSON deserialization is unaffected (System.Text.Json sees nested types regardless of file).

**Effort:** small (mechanical move). **Confidence: high.** Settings live closer to the domain code that consumes them.

### 5.3 SettingsWindow.cs split (3196 lines)

The biggest file in the codebase. `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` mixes:

- Theme construction (lines roughly 1-120 — agent estimate)
- Window lifecycle + event handlers (~120-250)
- ~50 page-builder methods (~250-1100)
- Search indexing + result UI (~1100-1400)
- Profile editor embed + utility methods (~1400-3196)

**Refactor:**

| New file | Responsibility | Approx LOC |
|---|---|---|
| `SettingsWindow.cs` (kept, slimmer) | Lifecycle, state, top-level coordination | ~600 |
| `Dialogs/SettingsThemeManager.cs` | ThemeBrushSet construction, palette | ~80 |
| `Dialogs/SettingsPageBuilders.cs` | Static page builders per category | ~700 |
| `Dialogs/SettingsSearchWidget.cs` | Index + filter UI | ~300 |
| `Dialogs/SettingsDialogHelpers.cs` | Profile-editor embed + button handlers | ~500 |

**Effort:** medium. **Confidence: medium** — line ranges are approximate (agent only sampled the file, didn't read 3,196 lines fully). Treat the splits as suggestive, validate against actual structure before cutting.

### 5.4 HistoryToolWindowControl.cs split (2201 lines)

Same shape as 5.3: monolithic WPF tool window. Suggested splits:

- `HistoryToolWindowControl.cs` (~400 lines, coordinator)
- `History/HistoryPanelBuilder.cs` (~200, BuildUi)
- `History/HistoryItemTemplates.cs` (~150, list templates)
- `History/HistoryEventHandlers.cs` (~400, selection/double-click/context)
- `History/HistoryCommands.cs` (~300, Open/Delete/Compare commands)

Effort: medium. Confidence: medium (line ranges are estimates).

### 5.5 AiRequestHandler.cs split (1892 lines)

Six pipelines under one roof: TextToSql, Explain, Fix, Optimize, Chat, IndexAnalysis. Plus retry/privacy/fallback infrastructure shared across them.

Suggested splits:

- `AiRequestHandler.cs` (~200, dispatcher only)
- `Ai/AiPrivacyValidator.cs` + `Ai/AiRetryPolicy.cs` (shared infra)
- `Ai/AiTextToSqlPipeline.cs`, `Ai/AiAnalysisPipeline.cs`, `Ai/AiIndexAnalysisPipeline.cs`
- `Ai/AiPromptBuilder.cs`, `Ai/AiProviderFallback.cs`

Effort: medium-large. Confidence: medium.

### 5.6 CompletionController.cs split (1466 lines)

Mixes IOleCommandTarget, debouncing, commit logic, wildcard expansion, native IntelliSense suppression. Each could be its own file:

- Keep CompletionController as IOleCommandTarget orchestrator (~200 lines)
- `Editor/Completion/CompletionKeystrokeFilter.cs`, `CompletionDebounceManager.cs`, `CompletionCommitHandler.cs`, `CompletionWildcardHandler.cs`, `CompletionPopupManager.cs`

Effort: medium. Confidence: medium.

---

## 6. Premature abstractions to collapse

| Item | Current state | Proposal |
|---|---|---|
| `SessionRequestHandler` | Pure delegation to `SessionManager` | Inline; have `SessionManager` implement `IMessageHandler` directly |
| `SignatureProvider` + `QuickInfoProvider` | Both single-impl, both produce symbol metadata for an offset | Merge into one `SymbolMetadataProvider` with a parameterized output mode |
| `HistoryRetentionService` | Wraps a single retention method | Inline as `HistoryDatabase.PruneOldEntriesAsync()` |
| `IAnalysisRule` × 120+ classes | Each rule = its own class | Long-term: data-driven (YAML/JSON) ruleset interpreter. Don't tackle until rule count keeps growing |

---

## 7. Module boundary issues

### 7.1 Engine ↔ Shell config coupling

`AiRequestHandler`, `HistoryRetentionService`, and other Engine handlers call `ConfigManager.Load()` directly. Engine has its own copy of `AkmlSql.Core.Config` types, so this *works*, but it means the Engine has implicit knowledge of the file format and refresh semantics that the Shell sets.

**Refactor:** introduce `IConfigProvider` injected at handler construction. Shell pushes config changes via the existing `AnalysisSettingsChanged` IPC pattern (already used for the safety-check settings cache in `ExecutionInterceptor`).

Effort: medium. Worth doing before adding more cross-cutting settings.

### 7.2 Shell UI imports raw IPC types

`CompletionController`, `HistoryToolWindowControl`, `ProfileEditorViewModel` directly reference `CompletionRequest`, `HistorySearchRequest`, `AiTextToSqlRequest`, etc. The Shell UI is coupled to the wire format.

**Refactor:** add a thin `ShellApiClient` wrapper that exposes domain methods (`RequestCompletion(text, offset) → CompletionItem[]`, `SearchHistory(filter) → HistoryEntry[]`). UI calls the domain API; the wrapper handles IPC serialization.

Effort: medium. Improves testability (mock `ShellApiClient`, run UI tests without engine).

### 7.3 `HistoryViewModel` reaches across the process boundary

`HistoryViewModel` calls `HistoryDatabase` methods directly (in-process). But `HistoryDatabase` lives in the Engine project — meaning either the boundary is being violated, or there's a duplicate class. (Worth verifying — if it's actually the Engine class being referenced from Shell, that's a real layering bug.)

If real: replace with `HistoryRpcClient` that goes through the existing `HistoryRequestHandler`. **Confidence: low** — the agent flagged this but we should verify the actual reference path before acting.

---

## 8. Top 5 quick wins (start here)

1. **Cache the three FontFamily allocations** (§3.2). 30 minutes total. Mechanical fix, satisfies CLAUDE.md WPF guidance.
2. **Decide P0 TODOs** (§1, items #1–3): either wire the signature/quick-info IPC or delete the skeleton classes. Both are concrete; pick one this sprint.
3. **Extract `CommandFactory.RegisterMenu`** (§3.1). One afternoon. Eliminates 80+ lines of boilerplate across 40 command files.
4. **Split `AppSettings.cs`** into per-domain files (§5.2). Mechanical move. No logic changes. Significantly easier navigation afterwards.
5. **`PipeRpcServer` dispatch table** (§5.1). One full day of work; biggest payoff. Adding new message types becomes a one-line change.

---

## 9. Confidence and caveats

- All findings in §1, §3.1, §3.2 were verified directly (grep, file inspection). **High confidence.**
- §5.3, §5.4, §5.5, §5.6 (large-file split proposals) used agent sampling rather than full reads. Line ranges are estimates — validate against actual structure before cutting.
- §6 and §7 are architectural opinions; reasonable people will disagree on what's "premature" abstraction. Treat them as conversation starters.
- This audit deliberately did **not** flag the 6 thin shell-target wrappers as duplication — that's the multi-SDK strategy. Don't merge them.
- This audit deliberately did **not** propose collapsing the .NET Framework / .NET 10 process boundary — that's the engine-isolation strategy.
