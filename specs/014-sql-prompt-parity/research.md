# Phase 0 Research: SQL Prompt Parity

**Branch**: `014-sql-prompt-parity` | **Date**: 2026-04-09

This document records the technical decisions taken during Phase 0 planning for spec 014. Every entry follows the **Decision / Rationale / Alternatives Considered** triplet. There are no `NEEDS CLARIFICATION` markers in the Technical Context — every choice below is deliberate.

---

## R-001 — Pre-execution interception strategy (US1, FR-001)

**Decision**: Hook the four execute commands (`Edit.RunQuery` `F5`, `Query.ExecuteCurrentStatement` `Shift+F5`, `Query.ExecuteCurrentBatch` `Alt+Shift+F5`, `Query.ExecuteToCursor` `Ctrl+Shift+F5`) via `IOleCommandTarget` chain interception in a per-text-view command filter MEF export, parse the about-to-run text statement-by-statement on the engine side via `SafetyCheckRequest`, and surface a modal WPF `SafetyWarningDialog` from the shell.

**Rationale**:
- `IOleCommandTarget` chaining is the only documented VS extensibility surface that lets a 3rd-party extension intercept the Execute command before SSMS sends it to SQL Server. SSMS does not raise its own `BeforeExecuteQuery` event for extensions.
- Filtering on the shell side keeps the cross-host code in `AkmlSql.Shell.Shared` and avoids per-host VSCT divergence.
- The engine already has a `SafetyCheckHandler` that detects DELETE/UPDATE without WHERE; extending it to cover MERGE without WHEN MATCHED, INNER JOIN bodies, and procedure/trigger bodies is mechanical (`TSqlFragmentVisitor` walks).
- The modal WPF dialog defaults focus to **Cancel** (FR-005) and renders the environment color in the header (FR-008) by reading the existing `TabColoringManager` state for the active server.

**Alternatives Considered**:
- *Hook `Microsoft.SqlServer.Management.Smo` events*: rejected because SMO is not present in VS hosts and the events fire after `T-SQL` is sent.
- *Reflection into SSMS internals (`Microsoft.SqlServer.Management.UI.VSIntegration.QueryWindow`)*: rejected — version-specific, breaks across SSMS 20/21/22, fragile.
- *Run the safety check on the shell side via in-process `TSql170Parser`*: rejected because the parser is engine-side only (CLAUDE.md "Process Boundary"). Parsing on the shell would duplicate logic and violate G2.

---

## R-002 — Column Picker integration into existing AkmlCompletionPopup (US2)

**Decision**: Extend `AkmlCompletionPopup` with a second `ContentPresenter` that hosts a new `ColumnPickerControl` user control. Switch between suggestion list and column picker via internal state (no second window). Selection state lives in a new `ColumnPickerSelection` POCO attached to the popup's `DataContext`.

**Rationale**:
- A1: the existing popup is already a WPF user control with the right z-order and `IInteractiveQuickInfoSession` integration. Adding a tab-style switch costs less than building a parallel popup.
- The Column Picker fires only when the cursor is in a SELECT-list / ORDER-BY / GROUP-BY / column-insertion context (`CursorContext.ClauseType` already detects these).
- Multi-selection state is transient and lives only while the picker is open — no persistence (Key Entity: Column Picker Selection).

**Alternatives Considered**:
- *New floating tool window*: rejected — breaks the keyboard-only workflow and forces the user to mouse over.
- *Reuse the SSMS / VS QuickInfo window*: rejected — that window is owned by the editor and cannot host arbitrary WPF children safely across all 6 SDKs.

---

## R-003 — Wildcard expansion `*`+`Tab` wiring (US3)

**Decision**: Add a `IOleCommandTarget` filter that intercepts `Edit.Tab` (cmdID `VSStd2KCmdID.TAB`), checks whether the immediately preceding non-whitespace character is `*` (or `alias.*`), and if so dispatches a `WildcardExpansionRequest` to the engine and replaces the `*` with the response. If the cursor is not in that context, the filter returns `OLECMDERR_E_NOTSUPPORTED` so the next command target handles Tab normally.

**Rationale**:
- `WildcardExpansionHandler` already exists on the engine side and is wired to the existing `Ctrl+B, Ctrl+W` chord.
- Tab is the highest-traffic key on the editor; the filter must be defensive — only act on the precise pattern, otherwise pass through.
- Detecting "immediately after `*`" is a 3-line text-buffer scan; no parser involvement needed.

**Alternatives Considered**:
- *Replace Tab globally via VSCT keybinding*: rejected — breaks indent and snippet expansion.
- *Use a `ITextSnapshot` post-change handler*: rejected — fires after Tab has already inserted whitespace, polluting undo.

---

## R-004 — Command Palette result aggregation (US4)

**Decision**: Build the palette around a `CommandPaletteEntry` interface and four `ICommandPaletteSource` implementations (`AkmlCommandSource`, `AkmlOptionsSource`, `HostCommandSource`, `DatabaseObjectSource`). Each source returns lazily-enumerated entries; the palette window applies fuzzy ranking via the existing `FuzzyMatcher` and merges results live as the user types. The `DatabaseObjectSource` is registered only on SSMS hosts (per FR-048).

**Rationale**:
- The four-source split keeps every category independently testable and allows the SSMS-only `DatabaseObjectSource` to fail gracefully on VS hosts.
- The existing `FuzzyMatcher` (in `AkmlSql.Engine.Completion.FuzzyMatcher`) is already proven on completion and gives the same scoring across all categories.
- Lazy enumeration keeps the palette responsive even when the database has thousands of objects (FR-049 ranking happens incrementally).

**Alternatives Considered**:
- *Pre-build a single combined index at startup*: rejected — invalidation across connection changes and SSMS command sets is complex; lazy is simpler.
- *Use SSMS's built-in Command Window*: rejected — it's a text-only command bar, not a fuzzy palette, and has no extensibility for AKML SQL options.

---

## R-005 — Tab coloring storage and propagation (US5)

**Decision**: Store environment definitions in `AppSettings.TabColoring` (a new section) as a list of `Environment` records (Name, ColorHex, GradientEnabled, Label) plus a list of `TabColorAssignment` records (Scope = Server / Database / ServerGroup, ScopeValue, EnvironmentName). The shell-side `TabColoringManager` listens for connection-changed and tab-activated events and re-renders the affected tab header by setting WPF brushes on the matching `MdiChild` border.

**Rationale**:
- A2: the existing `EnvironmentDetector` and `TabColoringManager` infrastructure already runs but lacks the UI to assign colors. The decision keeps the storage path consistent with all other settings (G5).
- Inheritance order: server-level assignment overrides server-group assignment overrides database default (FR-045).
- Live re-render avoids restart (FR-042).

**Alternatives Considered**:
- *Per-server registry entries*: rejected — violates G7 (never write registry).
- *Use SSMS's built-in tab coloring*: rejected — SSMS only colors by server status (production red), not by user-defined environments, and has no API for extension code to write to it.

---

## R-006 — Code Analysis Issues window data flow (US6)

**Decision**: Add an `AnalysisIssuesPushHandler` on the engine that, after every analysis run, sends an `AnalysisIssuesPushed` notification (no request-response, fire-and-forget) containing the full issue list for the active document. The shell-side `CodeAnalysisIssuesToolWindow` subscribes via `EngineLifecycle.Manager.Client.NotificationReceived` and rebuilds its WPF `DataGrid` on each push. Sorting/grouping/CSV export are pure shell-side WPF.

**Rationale**:
- The analysis already runs on every text change with debouncing; pushing the result is cheaper than the tool window polling.
- A `DataGrid` with `CollectionViewSource` gives sort + group + filter for free.
- CSV export is a 20-line `string.Join` over the visible rows.

**Alternatives Considered**:
- *Tool window polls the engine every N ms*: rejected — wasteful and lags behind typing.
- *Reuse SSMS Error List*: rejected — Error List APIs across SSMS 20/21/22 differ enough to be brittle, and the spec wants a dedicated window with grouping and CSV export (FR-038).

---

## R-007 — `Ctrl+B` chord family wiring (US7, FR-028)

**Decision**: Define the seven new chords (`Ctrl+B, Ctrl+U/Q/W/C/B/I/E`) in each host's `.vsct` file under a single `<KeyBinding>` block per host, mapped to the same `CommandID`s in the shared `AkmlSql.Shell.Shared/Refactoring/CtrlBChordHandler.cs` file. Chord handlers dispatch to the existing `RefactoringEngine` request types (Apply Casing, Qualify Object Names, etc.) — every routine already exists per A4.

**Rationale**:
- One chord wiring per host is unavoidable because each host has its own `.vsct` GUID set, but the C# command implementations live once in the shared project.
- Wiring at the VSCT level is the only way to bind chords with the host-specific `<Editor>` scope so they don't shadow global keybindings.

**Alternatives Considered**:
- *Register chords at runtime via `IVsCommandWindow`*: rejected — chord bindings registered at runtime do not survive SSMS restarts and are not discoverable.
- *Use the existing menu infrastructure to expose them*: not rejected — menus are also added (FR-029), but the chord bindings are independent.

---

## R-008 — Object Definition Box rendering (US8, FR-020..FR-024)

**Decision**: The `ObjectDefinitionBox` is a docked WPF `UserControl` rendered to the right of the `AkmlCompletionPopup`. It has two tabs: **Summary** (a `DataGrid` of column metadata) and **Script** (a read-only AvalonEdit-style code view, but using the existing `AkmlSql.Shell.Shared.Editor.SyntaxColoringTextBox` we already ship). The control persists its size in `AppSettings.CompletionPolish.ObjectDefinitionBoxSize` (FR-023). The `Ctrl`-held semi-transparency (FR-024) is a single `Opacity` setter on both popups via a global `KeyDown`/`KeyUp` listener attached to the editor.

**Rationale**:
- Reusing `SyntaxColoringTextBox` avoids a third-party dependency on AvalonEdit (which is not currently in the manifest).
- Persisting size in `config.json` is consistent with G5.
- The Ctrl-held listener is process-wide for the active editor — exactly what SQL Prompt does.

**Alternatives Considered**:
- *Render the definition inline in the suggestion popup*: rejected — the popup is already height-constrained and the definition needs scroll space.
- *Open in a new tool window*: rejected — too far from the editor for a peek-and-type workflow.

---

## R-009 — Inline `-- akml-format off` action wiring (US9)

**Decision**: Add a single `ITextActionListProvider` in `AkmlSql.Shell.Shared.Editor` that contributes a "Disable formatting for selected text" entry when there is a non-empty selection. The action wraps the selection with `-- akml-format off\n` and `\n-- akml-format on` markers using the active editor's `ITextEdit` API. The existing `NoformatScanner` already honors these markers.

**Rationale**:
- A8: `NoformatScanner` is already in place. The only missing piece is the editor action that inserts the markers.
- Single `ITextEdit` is atomic and undoable.

**Alternatives Considered**:
- *Right-click context menu entry only*: rejected — discoverable via `Ctrl` action list per the SQL Prompt convention; the menu entry can be added later.
- *Custom WPF popup*: overkill for a one-action surface.

---

## R-010 — AI keyboard shortcuts vs. existing AI menu items (US10, FR-053..FR-057)

**Decision**: Add four new VSCT command IDs in each host's `.vsct` (`AiOpenChat`, `AiFixSelection`, `AiOptimizeSelection`, `AiManualGhostText`) bound to the four chords from the spec. Their handlers all call into the existing shell-side `AiChatPanelService` (which already wraps `AiRequestHandler` IPC calls). When AI is disabled (`AppSettings.Ai.Enabled == false`), the handlers post a `StatusBarManager.SetMessage("AKML SQL AI is disabled in Settings")` and return.

**Rationale**:
- The chords are independent of the menu items and discoverable in the Command Palette (US4).
- The status-bar message satisfies FR-057 ("brief status-bar message").

**Alternatives Considered**:
- *Bind chords to the existing menu commands directly*: rejected — the menu commands target the active document, the chord-style binds need text-view context, and the wiring is cleaner with dedicated command IDs.

---

## R-011 — Dual-instance awareness regression guard (US11)

**Decision**: Add an integration test in `tests/AkmlSql.Engine.Tests/Connection/SsmsConnectionDetectorRegressionTests.cs` that simulates two text views with two different file paths and asserts that `TryDetectConnection(textView)` never falls back to `ActiveDocument`. The fix from commit `2c34133` is the source-of-truth implementation; this test pins it.

**Rationale**:
- The bug is already fixed; the spec story exists to prevent regression. A targeted test is the right artefact.
- The test stubs `EnvDTE.Documents` so it runs without an SSMS host.

**Alternatives Considered**:
- *Manual test only*: rejected because SC-005 demands 100% coverage across 50 sequential runs and a manual test cannot ensure that.

---

## R-012 — Settings surface mapping (US12)

**Decision**: Every new toggle flows through a single new section per area in `AppSettings`:

| Section | Toggles |
|---|---|
| `ExecutionWarnings` | `Enabled`, `WarnDeleteWithoutWhere`, `WarnUpdateWithoutWhere`, `WarnInsideJoin`, `WarnInsideProcOrTrigger` |
| `TabColoring` | `Enabled`, `GradientEnabled`, `Environments[]`, `Assignments[]` |
| `CommandPalette` | `Enabled`, `IncludeHostCommands`, `IncludeDbObjects`, `MaxRecentItems` |
| `Ai` | `Enabled`, `OpenChatShortcut`, `FixShortcut`, `OptimizeShortcut`, `GhostTextShortcut`, `EnableExplainSql`, `EnableQueryIndexAnalysis`, `EnableCommentToSql`, `EnableFixOnError`, `ShowEditorIcon`, `ShowFollowupSuggestions` |
| `CompletionPolish` | `SuggestionsSuppressed` (transient — runtime only), `CommitKeys[]`, `EnableCategoryFilter`, `EnableMsDescription`, `EnableParameterHighlight`, `EnableEncryptedDecryption`, `EnableTempTableIntellisense`, `ObjectDefinitionBoxSize` |
| `ResultGrid` | `EnableCopyAsInClause`, `EnableScriptAsInsert`, `EnableOpenInExcel`, `OpenInExcelPreservePrecision` |
| `Lightbulbs` | `Enabled`, `ShowAdvisoryHints`, `EnableApplyFixForRules` |
| `Navigation` | `EnableF12ScriptAsAlter`, `EnableCtrlF12SelectInOe`, `EnableSummarizeScript`, `EnableFindUnused` |

**Rationale**:
- One nested section per feature area keeps the JSON readable and gives the Options window a natural one-page-per-section layout (FR-058).
- The existing `ConfigManager` already round-trips arbitrary nested POCOs.
- Search (`FR-059`) is a flat reflection walk over `AppSettings` properties using the `[Description]` attribute already in use.

**Alternatives Considered**:
- *One flat dictionary*: rejected — every value would need a manual key, defeating type safety and refactor support.
- *Per-feature config files*: rejected — A12 says single source of truth.

---

## R-013 — Smart Rename transactionality (US15, FR-071)

**Decision**: The rename Apply path runs all generated `sp_rename` and `ALTER` statements inside a single `BEGIN TRAN ... COMMIT/ROLLBACK` wrapping in `SchemaMetadataService.ApplySmartRenameAsync`. Any exception triggers `ROLLBACK` and the original schema is preserved. The IPC response includes a `Status` enum (`Applied`, `RolledBack`, `Cancelled`) and a `RolledBackReason` string when applicable.

**Rationale**:
- `sp_rename` is a DDL statement and SQL Server allows DDL inside an explicit transaction; rollback on failure is correct.
- The transaction guarantees the spec's "zero broken dependents" success criterion (SC-013).
- The `Status` enum tells the shell whether to refresh the schema cache after Apply.

**Alternatives Considered**:
- *Apply each rename in its own transaction*: rejected — partial application leaves the database in an inconsistent state.
- *Use `sp_rename` only and skip ALTER on dependent views*: rejected — dependent views with `WITH SCHEMABINDING` need to be `ALTER`-ed to pick up the new column name.

---

## R-014 — Find Invalid Objects scan strategy (US14, SC-012)

**Decision**: `SchemaMetadataService.ScanInvalidObjectsAsync` queries `sys.sql_expression_dependencies` joined to `sys.sql_modules`, filters where the referenced object is missing, and yields results in chunks of 50 via `IAsyncEnumerable`. The shell-side `InvalidObjectsToolWindow` consumes the stream and renders rows live so users see partial results within 2 s (SC-012 / FR-065).

**Rationale**:
- `sys.sql_expression_dependencies` is the canonical SQL Server view for broken-reference detection. It is faster than `sp_refreshsqlmodule` per object.
- `IAsyncEnumerable` over the IPC layer is supported by MessagePack via chunked notification messages — same pattern used by the existing schema cache push.

**Alternatives Considered**:
- *Run `sp_refreshsqlmodule` on every object*: rejected — O(N) round trips to the server, too slow on a 5,000-object DB.
- *Cache the results across runs*: rejected — invalidation logic for "object created since last scan" is non-trivial; refresh button (FR-067) is sufficient.

---

## R-015 — AI fix-on-error toast hookpoint (US18, FR-086)

**Decision**: The `ExecutionInterceptor` (added for US1) also subscribes to the SSMS query-completed event chain via `IVsRunningDocumentTable3` cookie + `IVsTextViewCreationListener` and inspects the SSMS Messages pane for SQL Server error patterns. On a match, it surfaces a non-blocking `IInfoBarUIElement` toast in the document frame offering "Fix with AI". Clicking the toast opens the AI panel pre-filled.

**Rationale**:
- `IInfoBarUIElement` is the only documented VS extensibility for non-blocking notifications anchored to a document.
- Hooking the same `ExecutionInterceptor` keeps all execute-related interception in one class — easier to reason about, easier to test.
- The Messages-pane scan is robust because the SQL Server error format is stable across SSMS versions.

**Alternatives Considered**:
- *Use `IVsOutputWindowPane` events*: rejected — the events fire for any output, not specifically for query failures.
- *Wrap the SQL Server `RaiseError` in a try/catch in our own dispatch*: rejected — we don't dispatch the user's queries; SSMS does.

---

## R-016 — `#temp` table IntelliSense scope (US19, FR-100)

**Decision**: Add a new `TempTableProvider` registered in `CompletionEngine`'s provider list. The provider scans the active script's token stream for `CREATE TABLE #...` and `SELECT ... INTO #...` patterns up to the cursor position, parses the column declaration list, and contributes column completions when the cursor is inside an expression that references that temp table. Scope is per-script (the same `CursorContext` already used by `ColumnProvider`).

**Rationale**:
- Token-based parsing keeps the provider fast (no full AST walk).
- Per-script scope matches SQL Server's `#temp` semantics (session-scoped, but visible across all batches in the same script for editor purposes).
- The provider runs only when a temp table is referenced — zero overhead otherwise.

**Alternatives Considered**:
- *Send temp table parsing to the engine*: rejected — temp tables are editor-only state; the engine cannot see them and they should not pollute the schema cache.
- *AST-based detection*: rejected — `CREATE TABLE #x` parses fine but the column type list is awkward to walk in `TSqlFragmentVisitor`. Token scan is simpler.

---

## R-017 — Encrypted object decryption (US19, FR-098)

**Decision**: When the Object Definition Box's Script tab is requested for an encrypted procedure/function, `SchemaMetadataService.GetObjectDefinitionAsync` first tries `OBJECT_DEFINITION(OBJECT_ID(...))` (which returns NULL for encrypted objects), then if NULL and the user has DAC permission (verified by `HAS_DBACCESS('master')` + a check for `ADMIN:` connection prefix), opens a separate `Microsoft.Data.SqlClient.SqlConnection` to the DAC endpoint and reads `sys.sysobjvalues` to extract the encrypted bytes, applies the standard XOR algorithm against the decrypted RC4 key from `sys.sysobjvalues`, and returns the plaintext. The response includes a `WasDecrypted` flag so the shell can render the "decrypted" badge.

**Rationale**:
- The XOR + RC4 decryption is the documented technique used by every SQL tool (sp_decryptObject, ApexSQL, dbForge); it requires DAC and is read-only.
- Detecting DAC permission upfront avoids wasted connections.
- Returning `WasDecrypted = false` when DAC is unavailable lets the shell render the encrypted placeholder gracefully (FR-098).

**Alternatives Considered**:
- *Skip encrypted objects entirely*: rejected — encrypted procedure decryption is a SQL Prompt feature that AKML SQL must match (FR-098 explicit).
- *Bundle a third-party decryption library*: rejected — no MIT-compatible library exists; the algorithm is documented and easy to implement.

---

## R-018 — Result-grid action hook surface (US16, FR-074)

**Decision**: Use the `IVsTextViewFilter` chain on the SSMS results grid (which is itself an `IVsTextView` for grid mode in SSMS 21/22) plus a fallback `IVsRunningDocumentTable` cookie for the SSMS 20 grid (which uses an older `Microsoft.SqlServer.Management.UI.Grid.GridControl`). On context menu open, contribute three menu items: Copy as IN Clause, Script as INSERT, Open in Excel. The shell sends the selected rows' raw data to the engine via `ResultGridScriptRequest`, and the engine returns the formatted clipboard payload.

**Rationale**:
- SSMS 21+ grid is fundamentally an `IVsTextView`, which we already extend.
- SSMS 20's older grid needs a fallback path but the rest of the engine logic is reusable.
- Sending raw row data to the engine keeps the shell free of formatting logic and lets us unit-test it with `dotnet test`.

**Alternatives Considered**:
- *Reflect into `Microsoft.SqlServer.Management.UI.Grid.GridControl` to read selection*: rejected — version-coupled, brittle.
- *Render the menu items via the existing AKML SQL toolbar*: rejected — discoverability is much lower than right-click on the result.

---

## R-019 — F1 contextual help target resolution (FR-104)

**Decision**: Add an `F1HelpListener` MEF export that subscribes to the host `IVsHelpSystem` and inspects the focused element's `Microsoft.VisualStudio.Shell.PackageExtensions.HelpContextValues`. Each AKML SQL UI surface (Options page, dialog, tool window) sets a unique help context key like `akmlsql.options.completion`, `akmlsql.dialog.smartrename`, `akmlsql.window.invalid-objects`. The listener maps each key to a documentation URL and opens it in the system browser.

**Rationale**:
- `IVsHelpSystem` is the canonical VS help integration, supported on all 6 hosts.
- Static URL mapping (a `Dictionary<string, string>` in code) is the simplest source of truth — easy to update with each release.

**Alternatives Considered**:
- *Open a single landing page on F1*: rejected — defeats the "find this exact feature" UX (FR-104).
- *Bundle help content offline*: rejected — adds installer bloat; the docs live online and update independently.

---

## R-020 — Browse Open Tabs `Ctrl+Q` source (FR-105, US20)

**Decision**: The `BrowseOpenTabsDialog` enumerates `EnvDTE.DTE.Documents` (filtering to `.sql` and `.tsql` extensions), shows a fuzzy-search input box at top, ranks via `FuzzyMatcher`, and on Enter calls `EnvDTE.Document.Activate()` on the selected entry. The dialog is a modal WPF window owned by the host main window.

**Rationale**:
- `EnvDTE.Documents` is the only cross-host way to enumerate open document tabs.
- Activate-on-enter is two lines of code.
- Modal WPF is justified because the action completes within seconds and dismissing the dialog is the natural exit.

**Alternatives Considered**:
- *Tool window listing open tabs*: rejected — overkill, and the tool window itself becomes another tab to manage.
- *Reuse `IVsUIShellOpenDocument`*: rejected — that's for opening files, not switching among open ones.

---

## Summary of resolutions

Every NEEDS CLARIFICATION has been resolved. There are zero deferred questions. Phase 1 can proceed.
