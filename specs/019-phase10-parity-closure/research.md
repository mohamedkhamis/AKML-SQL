# Phase 0 Research: Phase 10 — SQL Prompt Parity Closure & Bug Fixes

This document resolves the design unknowns surfaced during planning. Each entry is structured as **Decision / Rationale / Alternatives considered**, focusing on choices that have material implementation impact. Items already locked down by the spec or by inherited infrastructure (e.g., MessageType integers reserved by spec 014, DPAPI key storage shipped by spec 015, `ThemeRegistry` shipped by spec 016) are noted as "no research needed" so this document tracks only the open decisions.

## R-001: Multi-select Column Picker inside `AkmlCompletionPopup` (US2)

**Decision**: Add a second `ContentPresenter` to `AkmlCompletionPopup.xaml` (code-built) that hosts `ColumnPickerControl` as a sibling of the existing suggestion list. The popup runs a small state machine with two modes — `Suggestions` and `ColumnPicker` — switched by `Ctrl+Left Arrow` (enter picker) / `Ctrl+Right Arrow` (return to list) / `Esc` (close both). The `ColumnPickerControl` is a WPF `ListBox` with a `DataTemplate` per row containing a `CheckBox` (multi-select), an icon for PK / FK badges, the column name, and the type. `Space` toggles the row's checkbox; `Ctrl+A` selects every visible row; `Enter` or `Tab` commits all checked rows by computing the comma-separated insertion string and invoking `ITextBuffer.Replace` on the parent text view.

**Rationale**: Reusing `AkmlCompletionPopup` matches spec 014 A9 ("the existing completion popup control … is extended to host the Column Picker tab … rather than being replaced"). The two-mode state machine is the smallest delta that satisfies FR-007..FR-010 without disturbing the existing single-select flow. Column metadata is already in the Phase A / Phase B schema cache; the picker reads `DatabaseCache.GetTable(schema, name).Columns` directly with no new IPC.

**Alternatives considered**:
- **Separate `ColumnPickerWindow` as a modal**: rejected — modal dialog interrupts the typing flow and contradicts the SQL Prompt UX the spec mirrors (FR-010: "closable via `Esc` without inserting" implies non-modal popup behaviour).
- **In-popup multi-line "selected columns" footer + click-to-toggle in suggestion list**: rejected — too far from SQL Prompt's mental model; `Space` to multi-select is the established convention from FR-013.
- **Virtualize the picker for ≥ 500 columns via `VirtualizingStackPanel`**: deferred to implementation; not a research-time decision. The `ListBox` default panel is `VirtualizingStackPanel` so this is free; only re-evaluate if the spec edge case "Column Picker with 500+ columns" surfaces a regression.

## R-002: `Tab` after `*` inline wildcard expansion via `IOleCommandTarget` filter (US2)

**Decision**: Create `TabWildcardExpansionFilter` as a MEF `[Export(typeof(IVsTextViewCreationListener))]` that hooks the text view's `IOleCommandTarget` chain. On `cmdidTab`, the filter inspects the character immediately before the caret. If that character is `*` (or `*` is preceded by `<identifier>.`), it issues a `WildcardExpansionRequest` via `PipeRpcClient` and replaces the asterisk (or `alias.*`) span with the response's column list. Otherwise the filter returns `OLECMDERR_E_NOTSUPPORTED` to allow the normal Tab handler to run (indent or completion-commit).

**Rationale**: `IOleCommandTarget` is the canonical VS extensibility hook for keystroke interception; the safety dialog and execution interceptor already use the same pattern (`ExecutionCommandFilter`). The engine path (`WildcardExpansionHandler`) is unchanged, satisfying spec 014 A3.

**Alternatives considered**:
- **`KeyDown` event handler on the text view's WPF element**: rejected — does not see the same keystrokes the editor consumes; bypasses the existing command priority chain.
- **Reuse the existing `Ctrl+B, Ctrl+W` chord handler with a different binding**: rejected — `Tab` is not a chord, it must be intercepted at the `IOleCommandTarget` layer; chord routing happens after this filter.

## R-003: Code Analysis Issues window — tool window registration across 6 hosts (US3)

**Decision**: Register `CodeAnalysisIssuesWindow` as an `[Export(typeof(ToolWindowPane))]` MEF export with `GuidAttribute("…")` and a `[ProvideToolWindow]` attribute on the host's `AkmlSqlPackage`. Use the same registration pattern as `HistoryToolWindow` and `AiChatToolWindow`. The window's WPF content is a `ThemeAwareUserControl` with a `DataGrid` whose `ItemsSource` is bound to an `ObservableCollection<AnalysisIssueDisplayRow>` populated by the existing `AnalysisController`'s `AnalysisCompleted` event. CSV export is a button that opens a `Microsoft.Win32.SaveFileDialog` and writes via `CsvHelper`-style manual emission (no NuGet dep — UTF-8 + BOM + RFC 4180 quoting). Docked-position persistence is automatic via `[ProvideToolWindow(... Style=VsDockStyle.Tabbed, MultiInstances=false)]`.

**Rationale**: Matches the established AKML SQL tool-window pattern. No new IPC — the engine's `AnalysisRequest` / `AnalysisResult` types already carry every column the window needs. Auto-refresh on pause-of-typing uses the existing debounce in `DiagnosticTagger` (already 500ms — pair with a 500ms additional UI tick to land within FR-012's 1-second budget).

**Alternatives considered**:
- **Reuse the SSMS Error List integration shipped in spec 015 US2**: rejected — Error List is non-dockable in tabbed form, doesn't support custom columns/grouping, and can't show CSV export. The Issues window is purposefully a side-by-side companion, not a replacement.
- **Standalone WPF `Window`**: rejected — not dockable across SSMS restarts (loses spec 014 US6 acceptance scenario 5 and FR-040).

## R-004: Lightbulb Details popup + Apply Fix mechanism (US3)

**Decision**: Add `LightbulbDetailsPopup` as a WPF `Popup` (not a dialog) anchored to the squiggle's caret rectangle. Trigger: `IClassifierProvider` registers a key-event listener that watches for `Ctrl` modifier + mouse hover over a `DiagnosticSpan`. The popup is a small `Border` with three text rows (Rule ID + Severity, Problem statement, Remediation paragraph) and a button row (`Apply Fix` for auto-fixable rules, `Disable this rule` always present, `Dismiss` always present). `Apply Fix` calls `RefactoringEngine.ApplyFixAsync(ruleId, span, edit)` which returns an `ITextEdit` to commit on the active text buffer. For rules whose fix needs schema metadata not yet loaded (Phase B in progress), the popup queues the fix in an in-memory `Dictionary<DiagnosticSpan, FixDescriptor>` and shows a status-bar message; the fix is replayed when `SchemaCacheManager.PhaseBLoaded` fires.

**Rationale**: `Popup` (not `Window`) avoids stealing focus from the editor. The fix routines are the same ones the existing `Ctrl+B` chord family invokes (per spec 014 A17), so no new engine code. `Disable this rule` writes either an inline `-- akml-disable RuleId` comment at the top of the file (default) or appends to the per-project `.casettings` JSON (when a project root is detected) — both paths already exist.

**Alternatives considered**:
- **VS-native `ISuggestedAction` MEF**: rejected — VS 2017 (SSMS 20) does not expose the same suggested-actions API as VS 2022, and the AKML SQL approach has always preferred its own popup chrome (per `SafetyWarningDialog` precedent). Reusing one pattern across all 6 hosts is more valuable than relying on host-specific lightbulb APIs.
- **Inline `CompletionPopup` reuse**: rejected — semantically distinct (suggestions vs diagnostics); rendering rules different.

## R-005: Right-click query tab Tab Color submenus (US4)

**Decision**: Implement `TabContextMenuExtender` as an `[Export(typeof(IVsTextViewCreationListener))]` that on each text-view creation walks the WPF visual tree (`VisualTreeHelper.GetParent`) upward until it finds the `TabItem` / `DocumentTabItem` parent. Once found, hook the `ContextMenuOpening` event to inject three `MenuItem` instances at the top of the existing context menu: "Tab Color (Server)", "Tab Color (Database)", and (when the active server is in a Registered Server Group) "Tab Color (Server Group)". Each is a submenu populated dynamically from `AppSettings.Tabs.Environments`. Clicking an environment writes a new `TabColorAssignment` to `AppSettings.Tabs.Assignments` and invokes `TabColoringManager.RepaintAllTabs()`.

**Rationale**: The visual-tree-walk approach is established in `TabColoringManager` (spec 014 US5 commit `d7069d5`). MEF + `ContextMenuOpening` is the lowest-friction way to inject items without touching the host's own VSCT. The injection is non-destructive (existing host context menu items remain).

**Alternatives considered**:
- **VSCT-defined menu under `IDM_VS_CTXT_DOC_TAB`**: rejected — works in VS 2022 but SSMS 22's custom menu bar (`SSMSMnu.dll`) does not always honor it, per the SSMS 21 / 22 menu-bar issue documented in `CLAUDE.md`. Visual-tree injection is host-agnostic.
- **Standalone "Tab Coloring…" command in the AKML SQL menu**: rejected — requires the user to know which tab they want to color first; SQL Prompt's UX is right-click-on-the-tab and the spec's FR-041 mirrors that.
- **High Contrast WCAG-AA clamp**: implemented via `SystemParameters.HighContrast` check + per-color luminance-clamp helper. When `HighContrast == true`, the rendered tab color is darkened (`Color.FromArgb`, multiply each channel by 0.5) and the foreground is forced to `SystemColors.HighContrastForegroundColor` to guarantee 4.5:1 against the clamped background.

## R-006: Command Palette four-source aggregation (US6)

**Decision**: Define `ICommandPaletteSource` as `IEnumerable<CommandPaletteEntry> GetEntries(string query)`. Concrete sources: `AkmlCommandSource` enumerates `OleMenuCommandService.AllCommands`; `AkmlOptionsSource` reflects over `AppSettings`-derived metadata using a new `[CommandPaletteEntry(Label, Path)]` attribute added to settings properties (added gradually as part of US12 / settings audit); `HostCommandSource` enumerates `EnvDTE.DTE.Commands` once per session (cached); `DatabaseObjectSource` (SSMS only) reads from `DatabaseCache.Tables`/`Views`/`Procedures`/`Functions` filtered by the in-flight query string. The window aggregates via `IEnumerable.Concat`, ranks via the existing `AkmlSql.Engine.Completion.FuzzyMatcher`, and renders each row with a small category-badge `Border` (filled from `ThemeTokens.Surface.Badge<Category>`). Most-recent items are persisted as a `List<string>` in `AppSettings.CommandPalette.RecentItems` per host (the existing setting key, extended).

**Rationale**: The interface-based source plug-in pattern matches the established engine pattern (`IAnalysisRule`, `ICompletionProvider`). Reusing `FuzzyMatcher` satisfies spec 014 A7. Caching `DTE.Commands` avoids the per-keystroke COM round-trip cost.

**Alternatives considered**:
- **Single hard-coded combined source**: rejected — couples sources to the window, prevents future additions (e.g., a "recent files" source).
- **Per-source query-time filtering inside `FuzzyMatcher`**: rejected — separates ranking from source-specific result generation; harder to add per-source weights (Options entries should rank slightly behind commands of the same score).

## R-007: Find Invalid Objects — engine handler implementation (US8)

**Decision**: `FindInvalidObjectsHandler` (.NET 10 engine, MessageType 90) uses `sys.sql_expression_dependencies` joined to `sys.objects` to detect references to non-existent objects, plus `sys.sql_modules` parsed via the existing `TSql170Parser` to surface line numbers for the error site. The query batches at 100 objects per SQL round-trip and streams `InvalidObjectRecord` results back to the shell as multiple `FindInvalidObjectsResponse` chunks (handler returns the first chunk synchronously plus background `Notification` messages for subsequent chunks). The shell's `FindInvalidObjectsWindow` displays each chunk on arrival, satisfying the streaming requirement in FR-036.

**Rationale**: `sys.sql_expression_dependencies` is the standard SQL Server catalog view for this analysis and exists from SQL Server 2008 onward (AKML SQL's minimum target). Batched + streaming avoids both 30-second timeout risk and "freeze the window for 30 seconds" UX risk.

**Alternatives considered**:
- **`sys.sql_dependencies` (deprecated)**: rejected — deprecated, missing schema-bound entries.
- **In-engine T-SQL AST analysis**: rejected — would require resolving every identifier against the schema cache, which is slower and duplicates server-side metadata work.
- **Single-batch synchronous response**: rejected — fails FR-036's 2-second partial-results requirement on large databases.

## R-008: Smart Rename transactional script generation + dependency preview (US10)

**Decision**: `SmartRenameHandler` (.NET 10 engine, reuses existing `RefactorPreviewRequest` / `RefactorApplyRequest` types per spec 014 Phase 2 audit findings) generates a script in three sections:

1. **Pre-rename validation** — `BEGIN TRANSACTION; ... ROLLBACK TRANSACTION;` block that probes for name collisions via `OBJECT_ID('schema.newName')`.
2. **Rename via `sp_rename`** — for tables/columns; **drop-and-recreate** for procedures/functions/views/triggers because `sp_rename` doesn't update dependent definitions.
3. **Dependent object updates** — for every dependent object found via `sys.sql_expression_dependencies`, generate an `ALTER` statement with the new name substituted by the engine's existing `TSqlFragmentVisitor` rewrite path.

The whole script is wrapped in `BEGIN TRANSACTION; ... COMMIT;` with a `TRY/CATCH` that issues `ROLLBACK` on any failure. The preview dialog gets three tabs:
- **Actions** — the generated script as syntax-highlighted T-SQL.
- **Warnings** — name collisions, extended-property breakage, permission preservation notes.
- **Dependencies** — flat list of every dependent object with its line/column in the new script.

**Rationale**: `sp_rename` is the SQL Server idiom for tables/columns but is unsafe for views/procedures (it doesn't rewrite the bodies). Drop-and-recreate is the only mechanism that ALSO preserves dependent-object correctness. The three-tab dialog matches SQL Prompt's UX one-to-one per FR-070. Transactional wrapping with `BEGIN TRY` / `BEGIN CATCH` satisfies FR-071's transactionality.

**Alternatives considered**:
- **`sp_rename` only**: rejected — leaves dependent views/procedures with stale references.
- **Drop-and-recreate everything**: rejected — loses extended properties + permissions, fails FR-072.
- **Separate transaction per dependent object**: rejected — partial failure leaves database in inconsistent state, fails FR-071.

## R-009: AI selection-icon adornment at the right edge of selection (US13)

**Decision**: `AiSelectionIconAdornment` is an `IWpfTextViewMargin`-adjacent adornment registered via `[Export(typeof(IWpfTextViewCreationListener))]`. It subscribes to the text view's `Selection.SelectionChanged` event. On selection commit (mouse-up or `Shift+Arrow` keyboard sequence stopping for ≥ 250ms), it positions a small WPF `Border` (16×16 px) containing the AI icon at the geometric right-edge of the last selection line via `IWpfTextView.ViewportRight - 24` and the line's `TextBounds.Bottom - 16`. Clicking the icon shows a small `Popup` with three buttons: Explain / Fix / Optimize, each invoking the corresponding existing AI command on the selection.

**Rationale**: `IWpfTextViewMargin` is overkill for a transient adornment; using an adornment layer added by the text view creation listener is the established pattern in AKML SQL (see `SchemaProgressMargin` for reference). 250ms debounce avoids flicker during keyboard selection.

**Alternatives considered**:
- **Inline icon at the caret**: rejected — interferes with the caret; not the SQL Prompt UX.
- **Right-margin glyph (like bookmarks)**: rejected — too far from the selection visually; users won't see it.

## R-010: PipeRpcServer dispatch table refactor (US14 FR-080)

**Decision**: Define `IMessageHandler` in `src/AkmlSql.Engine/Server/IMessageHandler.cs`:

```csharp
internal interface IMessageHandler {
    Task<RpcMessage?> HandleAsync(RpcMessage message);
}
```

Each existing handler class (already a field on `PipeRpcServer`) gets a thin wrapper `IMessageHandler` implementation that calls the appropriate method. The 55-case switch is replaced with a single `Dictionary<int, IMessageHandler>` populated in the constructor:

```csharp
_handlers = new Dictionary<int, IMessageHandler> {
    [MessageTypes.CompletionRequest] = new CompletionHandler(_completionEngine),
    [MessageTypes.SchemaStatusRequest] = new SchemaStatusHandler(_schemaCacheManager),
    // ...one entry per existing case
};
```

`DispatchAsync` becomes a four-line method (lookup, null-check, await, error-fallback). PipeRpcServer.cs drops from ~937 lines to ~250 lines (constructor + dispatch + frame loop + connection lifecycle).

**Rationale**: Matches the audit's recommended pattern (`codebase-audit-2026-05-05.md` §5.1). The wrapper-handler approach preserves the existing handler-class API (no internal refactor of e.g. `AiRequestHandler` or `SafetyCheckHandler` needed). Adding a new MessageType is a one-line dictionary insertion. Performance: dictionary lookup is O(1) and the switch's compile-time-jump-table is also O(1) in practice, so there is no performance regression.

**Alternatives considered**:
- **Attribute-based handler registration via reflection**: rejected — adds startup cost and obscures dispatch.
- **Source generator emitting the dictionary**: rejected — overkill for ~55 entries.
- **Keep the switch but extract per-case methods**: rejected — does not deliver the audit's stated goal of "adding a new MessageType requires zero changes to the server class".

## R-011: AppSettings.cs per-domain split (US14 FR-081)

**Decision**: Split `AppSettings.cs` (961 lines, 19 nested classes) into 19 sibling files under `src/AkmlSql.Core/Config/`. The root `AppSettings.cs` becomes ~150 lines containing only the top-level properties (one per nested settings section) plus `[JsonIgnore]` helpers and `EnsureDefaults()`. Each nested class moves to its own file with the same public class name (since they live in the same `AkmlSql.Core.Config` namespace, the JSON deserializer's `[JsonPropertyName]` references continue to work without change — `System.Text.Json` does not care about file location). Test impact: `tests/AkmlSql.Core.Tests/Config/AppSettingsRoundTripTests.cs` needs no signature changes; only the test fixtures may need a small touch if they expected `AppSettings` to be defined in the same physical file as a nested class.

**Rationale**: Pure mechanical refactor with zero behaviour change. `System.Text.Json` does not depend on physical file structure. Each settings file becomes navigable independently in IDE Solution Explorer, satisfying the audit's stated goal.

**Alternatives considered**:
- **Move nested classes to per-domain *folders*** (e.g., `Config/Completion/`, `Config/Analysis/`, etc.) — rejected as over-organisation for the size (19 files in one folder is fine; Solution Explorer collapses them well).
- **Convert to `partial class AppSettings`** — rejected; nested classes do not support `partial` decomposition the same way and the current design has each nested class as a distinct type, not a partition of `AppSettings` itself.

## R-012: F1 contextual help registration across every UI surface (US7 FR-104)

**Decision**: `F1HelpListener` (already shipped by spec 014 Phase 2 T020) gains a static `Register(string surfaceKey, string docPath)` API and a runtime hook: every `ThemeAwareWindow` and every dockable tool window calls `F1HelpListener.Register(GetType().Name, "doc/<surface>.md")` in its constructor. The listener's existing `Open()` method handles `cmdidF1Help` by computing the active surface's key (from focused `Window`/`ToolWindowPane`) and opening the registered `doc/` URL via `Process.Start`. A new `F1HelpRegistrations.cs` central file documents every registered key for review.

**Rationale**: One-line per-surface registration meets FR-104's "100% of surfaces" criterion without per-surface plumbing. The central registrations file makes coverage auditable.

**Alternatives considered**:
- **Attribute-based `[F1HelpKey("…")]` on each surface class**: rejected — invisible at code-review time; central file is more reviewable.
- **Reuse VS's native `IVsUserContext`**: rejected — context priorities are host-specific and would require six per-host implementations.

## R-013: Documentation hygiene mechanism (US1)

**Decision**: M0 ships as a single PR that (a) merges branch `018-options-dialog-phase2` to master, (b) updates the four docs listed in FR-002..FR-005, and (c) updates `CLAUDE.md` and `specs/014-sql-prompt-parity/tasks.md`. The PR's description includes a "before / after" table showing each updated section, so a reviewer can verify the change without re-reading the whole document.

**Rationale**: Single PR contains the merge plus the documentation; reviewers see one cohesive change instead of merge churn followed by doc cleanup.

**Alternatives considered**:
- **Separate PRs for the merge and the docs**: rejected — adds review burden and risks one being merged without the other.
- **Auto-generate the "shipped" sections of `progress.md` from `git log` annotations**: rejected — overkill for a one-time cleanup; future spec PRs already update `progress.md` by hand.

## R-014: Installer icon + banner (US5)

**Decision**: The assets at `src/AkmlSql.Installer/assets/icon.ico` and `assets/banner.bmp` already exist (per spec 015 commit `ec09c45` "Installer branding deferred; comment added, assets in place"). M1 updates `AkmlSqlSetup.iss` to reference them via `SetupIconFile=assets\icon.ico`, `WizardImageFile=assets\sidebar.bmp` (already referenced), and `WizardSmallImageFile=assets\banner.bmp` directives.

**Rationale**: Assets are present; the gap is the Inno Setup script not referencing them. One-line directive change per asset.

**Alternatives considered**:
- **Replace assets with redesigned ones**: out of scope — spec 015 assumed the existing assets are final.

## R-015: Items requiring NO research (already locked)

These design decisions are inherited from earlier specs and do not need Phase 0 work — they are listed here for completeness so the implementer knows not to revisit them:

- IPC frame format (`[4-byte length][4-byte XOR CRC][MessagePack(RpcMessage)]`) — locked by spec 002.
- MessageType integer allocations — locked by spec 014 Phase 2 audit; no new ints needed.
- DPAPI key storage with `dpapi:` prefix — locked by spec 015 US13 commit `ec09c45`.
- `ThemeRegistry` + `ThemeTokens` + `ThemeAwareWindow` infrastructure — locked by spec 016 Phase 1+2.
- Code-only WPF (no XAML) — locked by `.projitems` shared-project pattern.
- 8 WinForms dialogs out of scope — locked by spec 016 A-final.
- Implementation-first-with-test-backfill — locked by spec 014 tasks.md convention.
- Pre-execution safety dialog cancel-button discipline — locked by spec 014 US1 commit `f337729` + Phase 3b polish `db194e9`.
- Tab coloring core + rules editor — locked by spec 014 US5 commit `d7069d5`.
- `NoformatScanner` `-- akml-format off / on` markers — locked by spec 014 US9 engine work.
- `WildcardExpansionHandler` engine path — locked by spec 010.
- AI provider transport (`AiExplainRequest`, `AiIndexAnalysisRequest`, `AiTextToSqlRequest`) — locked by spec 009.

---

## Open clarifications

None. Every spec.md FR is resolvable from existing infrastructure plus the decisions captured above. No `NEEDS CLARIFICATION` markers remain.
