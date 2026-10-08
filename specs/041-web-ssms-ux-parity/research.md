# Research: SSMS-grade workspace for the web edition

**Feature**: 041-web-ssms-ux-parity | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)

Phase 0 of the plan. Eight parallel code investigations (results grid, value display and
export, execution engine, database and engine identity, workspace layout, settings, shell and
documents, test strategy) each read the code and the spec, and each was then checked claim by
claim by an independent skeptic that re-ran the probes (ScriptDom lexer, Microsoft.Data.SqlClient
against the local SQL Server, bUnit's package docs, Playwright's API manifest). Corrections the
skeptics proved are folded into the decisions below and marked **[corrected]**.

File references are repo-relative; line numbers are from 2026-10-08 on branch
`041-web-ssms-ux-parity` (base `c30cb6b`).

## Findings that changed the plan

| # | Finding | Effect |
|---|---|---|
| N1 | **`CommandBehavior.KeyInfo` rewrites every batch.** SqlClient prepends `SET FMTONLY OFF; SET NO_BROWSETABLE ON;`, so a batch starting with `CREATE PROCEDURE/VIEW/FUNCTION/TRIGGER` fails with Msg 111 ("must be the first statement in a query batch"). The web always sends `IncludeProvenance = true` (`src/AkmlSql.Web/Services/IQueryExecutionService.cs:63`), so today's engine cannot run such a statement at all (verified live). | The per-batch loop chooses the command behaviour per batch (R17); Parse always uses `Default` (R21). A pre-existing bug is fixed on the way. |
| N2 | **`DbColumn.NumericPrecision/Scale` are 255 when not applicable**, and the engine forwards 255 into `ColumnProvenanceDto` (`src/AkmlSql.Engine/Execution/ResultSetReader.cs:303-304`); the web copies it into every `CrudCellDto` and `CrudWriteGenerator` sets `SqlParameter.Scale = 255`. Editing a `date` column fails at Apply with "Scale value '255'…", a `money` column with "Arithmetic Overflow" (verified live). int/bigint/float/money carry a *real* precision with scale 255; datetime is 23/3, smalldatetime 16/0. | The declared-type composer whitelists by base type (R10) and the 255 sentinel is normalised to null in the same edit, fixing Apply on date/money columns. |
| N3 | **GO handling.** `TSql170Parser.Parse` splits `TSqlScript.Batches` at GO but rejects `GO 3` (error 46010); `GetTokenStream` emits `TSqlTokenType.Go` for a line-leading GO (also after a same-line block comment), never inside strings, comments or `[GO]`, and `GO;` yields Go + Semicolon. The repo already has `TsqlParserService.SplitBatches` (regex, ignores strings/comments, drops the repeat count) with no callers outside its tests (`src/AkmlSql.IntelliSense/Parser/TsqlParserService.cs:134-181`). | Extend `SplitBatches` into a token-stream splitter (R16) rather than add a parallel class. |
| N4 | **ScriptDom is already in the browser bundle**: `AkmlSql.Web` references `AkmlSql.IntelliSense` and three web services instantiate `TsqlParserService`. | The browser reuses the same splitter for the reply deadline (R22); server-side Parse is still right because FR-035 wants the server's verdict. |
| N5 | **`FireInfoMessageEventOnUserErrors`** delivers class ≤ 16 errors through `InfoMessage` with Number/Class/State/LineNumber in stream order and lets the reader continue (a failed SELECT yields an empty header-only set, as in SSMS); timeouts (-2) and class ≥ 17 still throw; a severity-20 error leaves `conn.State = Closed`. `SqlCommand.StatementCompleted` fires once per statement with `RecordCount` (SELECTs included), nothing under `SET NOCOUNT ON`; `reader.RecordsAffected` is cumulative DML only (verified live). | R18, R19. |
| N6 | **`SET PARSEONLY ON`** works as its own command on the persistent connection, keeps batch-relative line numbers and is session-sticky (verified live). | Parse = ON, batches, OFF under the gate; `conn.Close()` if OFF fails (R21). |
| N7 | **A database-listing IPC already exists** (`ListDatabases` 94/194, `src/AkmlSql.Engine/Handlers/Control/ListDatabasesHandler.cs`), but it needs a full connection string, and the web holds no SQL-auth password after connect (`src/AkmlSql.Web/Services/ISqlConnectionService.cs:105,127-129`). The engine keeps the session's connection string (`SessionState.ConnectionString`). The engine already uses `SqlConnection.ChangeDatabase` in two refactoring handlers. | R25, R26. |
| N8 | **Engine identity is decided by launch mode**: `--pipe … --parent-pid` (started by SSMS, runs as the signed-in user) vs `--web --config` (service or console); the installer creates the service as LocalSystem (`src/AkmlSql.Installer/web-installer.iss:104-113`), whose styles folder is the system profile's `%AppData%` (`src/AkmlSql.Formatting/Profiles/ProfileManager.cs:64-84`). Nothing on the wire says which kind the engine is. | `HostKind` on the handshake (R29); "Shared with SSMS" only for `ide`. |
| N9 | **The web schema cache is keyed by the raw `SqlConn.Server`/`Database` strings** everywhere that matters, and the whole UI already follows `SqlConnectionService.StateChanged` (MainLayout restarts SchemaSync, Editor retargets the tree, StatusBar relabels). | Database switches reuse `StateChanged` (R28); no re-key to the canonical identity in this feature. |
| N10 | **`CommandBehavior` aside, the execute path discards everything on error** (`src/AkmlSql.Engine/Execution/ExecuteQueryHandler.cs:106-110`), reports one cumulative row count (152-156) and sends only `Message` text; the web shows Messages only with > 1 result set and never names the History entry (`WebHistoryLogic.BuildRecordRequest` leaves `TabTitle` null; one `SessionKey` persists across reloads). | R17–R20, R64. |
| N11 | **History naming is engine-side**: a session is named on first insert — the `TabTitle` unless it is "scratch" (null, or `^(SQLQuery\d+\|[a-z0-9]{8})\.sql$`), in which case `query-NN` per local day; an auto name may be upgraded once; a user rename is never overwritten (`src/AkmlSql.Engine/History/QuerySessionStore.cs`, `QuerySessionNamer.cs`). The SSMS shell sends `TabTitle` only for saved files. | The web sends `TabTitle` only for user-named documents and a new `SessionKey` per New/Open (R64). |
| N12 | **The grid has none of the SSMS interactions** and its edit entry is double-click → `<input>` with `onchange` commit; **[corrected]** Escape does nothing today (it neither cancels nor commits); Enter and Tab commit through the native change event. Edits are keyed by *fetched* row index and `_rowIndexes` is already the seam `Virtualize` iterates (`src/AkmlSql.Web/Shared/ResultsGridComponent.razor:116,187-190,248-254`). | Sorting permutes `_rowIndexes` only (R6); Escape becomes cancel (R4). |
| N13 | **`ColumnSqlTypes` is the bare type name** (`GetDataTypeName`: `datetime2`, `decimal`) although the DTO comment promises `nvarchar(50)`; `ClrTypeHints` cannot tell date/datetime/datetime2 apart; `time` arrives as a string. | Declared type composed engine-side into the same field (R10). |
| N14 | **Clipboard writes already exist as bare `navigator.clipboard.writeText` calls** (History, AI chat) with no fallback; the Clipboard API needs a secure context, and the installer serves the web edition over plain http on a LAN. | `copyText` with an `execCommand('copy')` fallback (R5). |
| N15 | **bUnit 2.9.0 answers `Virtualize` and `FocusAsync` JS calls in both Strict and Loose mode** **[corrected]** (that is why the Pr247 test passes in Strict today); its Virtualize handler renders *all* items, not a viewport slice; `SetupModule` exists for `import` calls. | R68. |
| N16 | **Playwright's synthetic key presses never trigger browser accelerators** (no reload on F5/Ctrl+R even without `preventDefault`) **[corrected]**. | Key-capture tests assert `e.defaultPrevented` from an `AddInitScript` listener plus the execute side effect (R70). |
| N17 | **Theme tokens**: four `var(--akml-*)` names used in web CSS are undefined (`--akml-accent`, `--akml-status-error`, `--akml-surface-editor`, `--akml-history-match-highlight`) and fall back to hard-coded hex; `<style>` blocks exist in 26 razor files; the `IconBadge` and `TabColor` token groups are emitted but unused; typography tokens carry no font fallback (`Consolas` bare) **[corrected inventory]**. | R61 fixes the four names; R67 adds fallbacks in the generator; the CSS gate ratchets from the real inventory (R74). |
| N18 | **"Enable analyser" is saved but never read** (`src/AkmlSql.Web/Services/IAnalyserService.cs:75` hard-codes enabled); `AnalyserServiceTests` constructs the service with a null store; `Editor.razor:885` writes "No problems." whenever the issue list is empty. | R50 gates at the service *and* the page. |
| N19 | **`PersistCapsAsync` replaces the whole `ExecutionSettings` record** (`src/AkmlSql.Web/Pages/Editor.razor:778-779`), so any new field on it would be reset by the toolbar. | The toolbar stops persisting (session override, R52). |
| N20 | **`DiagnosticsRingBuffer.RestoreAsync` is not idempotent** (`src/AkmlSql.Web/Services/IDiagnosticsRingBuffer.cs:88-103`); Editor and Diagnostics both call it, duplicating entries. | Guarded in R56; the Diagnostics section must tolerate being mounted hidden. |
| N21 | **Modal focus helpers** in `akml-connection-manager.js` are generic except a single module-level stash slot and a `.akml-connmgr-panel` guard (`:15,:23`); a menu → dialog chain would lose the restore target **[corrected]**. | One shared `akml-ui.js` with a stash *stack* (X9). |
| N22 | **The Menu key and Shift+F10 dispatch a `contextmenu` event even when their `keydown` was prevented**; a per-cell `@oncontextmenu:preventDefault` leaves the grid root to open the browser menu **[corrected]**. | Root-level JS `contextmenu` listener, conditional on the inline input (R7). |
| N23 | **After an Apply that the page does not re-run** (non-single-SELECT batches, `Editor.razor:745`), the grid bakes committed rows and resets `_rowIndexes` to identity **[corrected]**. | Sort is re-applied (or cleared) after bake; display-coordinate selection is remapped (R6). |
| N24 | **`.gitignore:22` already ignores every `TestResults/` folder**; `Web.E2E` has no default category filter and `SiteScreenshotTour` is an untagged `[Fact]` against the deployed IIS site **[corrected]**. | R72 excludes the tour by name and commits text pass-lists, not `.trx`. |
| N25 | **IPC numbering**: the 200+ web-bridge band is *adjacent* (`ExecuteQuery 212 / ExecuteQueryResult 213 … ApplyChangesResult 216`), next free is 217; `AllMessageTypesInProcessTests` fails on an unregistered request code and on a registered `…Result` code, but silently skips codes with no request factory **[corrected]**. | X4 numbering; every new code gets a registration and a factory entry anyway. |
| N26 | **`akml-indexeddb.js` closes the connection on `onversionchange` but keeps the cached promise**, so a tab that yields to another tab's upgrade holds a closed database until reload; `onblocked` only fires for old-build tabs. **[corrected]** a reopen must omit the version argument (or use `event.newVersion`), otherwise the old tab's lower `DB_VERSION` throws `VersionError`. | Fixed with the version bump (R36); the "bump is dangerous" convention is overstated, but users with two tabs open see the "upgrade is blocked" message once after the update (release note). |
| N27 | **`SchemaRefreshRequest` (6) is a notification** (`SchemaRefreshHandler.ResponseMessageType => 0`; `SchemaRefreshComplete` 104 is a dead constant with no DTO). `SchemaRefreshService.Refresh` on a cache key that does not exist yet races `SchemaCacheManager.ReloadMissing` into two concurrent Phase A populates (duplicated objects). `ISchemaSync.ReportEditorActive` has no callers, so the 30 s checksum poll silently stops after five minutes; the engine "checksum" is `Phase:objectCount`, so a refresh that changes neither produces no drift. | The web Refresh action sends 6 with `SendNotificationAsync` and then forces a Phase A/B fetch (R28, R40); a database switch creates or claims the cache before refreshing (R26, R27); the editor re-arms the poll (R40). |
| N28 | **`FormatStylesSharedEngineTests` asserts the literal "Shared with SSMS"** against a sandbox engine it launches with `--web --config`, which R29 labels `console` → "On this engine". | The assertion changes by design (R69). |
| N29 | **Each project stamps its own build minute** unless one `-p:Version` is passed (release builds), so a web/engine version mismatch marker is lit in nearly every developer build; `AppVersion.Current` reads Core's assembly, not the web's. | The marker stays (FR-083) and the quickstart says why it shows in dev builds; the web segment reads the web assembly's version (R65). |

## Cross-area decisions

Eight investigations overlapped in places; these are the planner's arbitrations.

- **X1 — Declared type travels in `ColumnSqlTypes`, not in new keys.** The engine composes `datetime2(3)`, `decimal(10,2)`, `nvarchar(max)` into the existing string (its documented promise); the web parses it with one Core helper. The execution area's `ColumnPrecision/ColumnScale` keys are not added: provenance already carries precision/scale (with the 255 sentinel fixed), and the string also gives the header hover text. Money's four decimals come from the type name.
- **X2 — One GO splitter, in `TsqlParserService.SplitBatches`,** rewritten over the token stream. The engine uses it to execute; the browser uses the same method (ScriptDom is in the bundle, N4) for the reply deadline estimate. No `BatchSplitter.cs`.
- **X3 — Command behaviour per batch.** `KeyInfo` (provenance, editable grid) only for batches whose first token is not `CREATE/ALTER` + `PROC/PROCEDURE/VIEW/FUNCTION/TRIGGER/SCHEMA/DEFAULT/RULE` and that contain a `SELECT` token; otherwise `Default`. Parse mode is always `Default`.
- **X4 — Message numbers and capabilities.** `ChangeDatabase 217 / ChangeDatabaseResult 218`, `ExecuteParse 219 / ExecuteParseResult 220` (adjacent, 200+ band). Capabilities `session.database.v1` (ListDatabases-by-session, ChangeDatabase, `CurrentDatabase`) and `execute.v2` (structured messages, batches, Parse). `ExecuteQueryResult` keys: 8 `MessageDetails`, 9 `Batches`, 10 `CompletedAtUnixMs`, 11 `MessagesOmitted`, 12 `CurrentDatabase`; `ExecuteResultSet` key 11 `BatchIndex`; `HandshakeResponse` keys 8 `HostKind`, 9 `RunsAs`; `ListDatabasesRequest` key 1 `SessionId`.
- **X5 — Grid options live on `ExecutionSettings`** (`ShowColumnTypes`, `RetainLineBreaksOnCopy`, saved defaults only), not on a new store; the maximum column width stays a constant behind `GridColumnLayout`.
- **X6 — One grid per result set.** FR-021 (stacked sets, each with its own edits, Apply, widths and sort) and FR-015 (confirm before discarding) require per-set state; `ResultsGridComponent` splits into `ResultsPaneComponent` (tabs, stacking, messages, empty/running states) and `ResultSetGrid` (one per set). Existing test ids are kept and scoped inside `results-set-{n}`.
- **X7 — Dialogs and notifications are services hosted in `MainLayout`** (`IDialogService` + `DialogHost`, `INotificationService` + `NotificationHost`), so the Command Palette and every page can await a confirmation. The settings area's `ConfirmDialog` is this host, not a second component.
- **X8 — One workspace status service** (`IWorkspaceStatus`: execution phase, rows, elapsed, caret, document name, modified) replaces the two proposals (`IExecutionStatus`, `IWorkspaceStatus`).
- **X9 — JavaScript modules.** `akml-workspace.js` (page keys, splitters, fold-away observer), `akml-results-grid.js` (grid pointer capture, measurement, key trap, drag select), and `akml-ui.js` (the focus helpers moved out of `akml-connection-manager.js` with a stash *stack* and a panel-selector parameter, `trapTabStripKeys`, `copyText` with fallback, `downloadText`). `akml-connection-manager.js` re-exports from `akml-ui.js`, so its callers do not change. The command-palette module stays single-purpose.
- **X10 — Choices made for the user, flagged in the plan report:** Lucide icons (ISC) rather than Codicons (CC BY 4.0 artwork); saved files are UTF-8 with BOM; default-named documents appear in History as `query-NN` (SSMS's own behaviour for unsaved tabs); a batch timeout stops the remaining GO batches (SSMS behaviour; FR-025 amended 2026-10-08 to state it, together with the `GO SELECT 2` refusal); a GO line carrying other tokens (`GO SELECT 2`) refuses the whole script with SSMS's message; Ctrl+0 (Set to NULL) is claimed only while a grid cell has focus; "Open in editor" from History keeps the current session (not a new History entry); the AI reset does not clear chat history; Connections has no Restore defaults; Restore all defaults does not clear the schema cache or the log.

---

## Results grid

### R1 — Shared column widths: one CSS variable on the grid root

**Decision.** Keep the DIV `role=grid`, the sticky header, the `Virtualize` body, the CRUD
markers and every existing `data-testid`. Each row becomes `display:grid;
grid-template-columns: var(--akml-grid-cols); width:max-content; min-width:100%`, and the
grid root carries one inline `--akml-grid-cols` (row-number track first, e.g.
`44px 72px 236px 480px …`) emitted from a per-set `GridColumnLayout` (`int[] widths`).
Header, virtualised body rows and insert rows inherit the same tracks, so widths never
reflow (FR-006/FR-007) and row backgrounds span the scrolled width. `flex:1 0 120px` /
`min-width:80px` go away. Layout state is cleared in `ShowResult` (widths belong to the result
they were set on).

**Why.** One attribute on one element changes every row in a single layout pass; the drag
handler updates the same variable in JS for live feedback and hands .NET only the final
value; the DOM shape, sticky header (`results-grid.css:101-107`) and Pr247 ids are untouched.

**Rejected.** Inline widths per cell (rows × cols diffs, a .NET round trip per pointer move);
a `<table>` with `Virtualize.SpacerElement` (rewrites markup, CSS and ids); per-column
variables per cell; `<style>` text per set.

**Files.** `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (new, from `ResultsGridComponent`:
inline style on the root, `data-r`/`data-c` on cells, row numbers in the marker cell, both
render branches iterate `_rowIndexes`); `src/AkmlSql.Web/wwwroot/css/components/results-grid.css`
(row → grid tracks, drop flex sizing, `.akml-col-resizer`, row-number column);
`src/AkmlSql.Web/Shared/Grid/GridColumnLayout.cs` (new: `MinColumnPx=48`, `MaxColumnPx=480`,
`DefaultColumnPx=120`, row-number width by digit count, `ToCssTracks()`).

**Risks.** `width:max-content` on the Virtualize spacer divs must stay height-only (verify in
browser); any leftover flex rule on `.akml-results-cell` fights the tracks.

### R2 — Auto-fit: C# picks candidates, JS measures with canvas

**Decision.** C# scans every row per column by display-text length (cheap, exact for
monospace) and keeps the header plus the three longest candidates; one call
`measureColumns(rootEl, headerTexts, candidates)` measures them with a cached offscreen
canvas whose `ctx.font` is read from a probe header cell and a probe body cell
(`getComputedStyle`), so zoom and font fallback are honoured; C# adds padding and the
sort-glyph/resizer allowance and clamps to `[max(48, min(headerPx, 480)), 480]` so the header
sets the floor. Runs after `ShowResult`, on "Auto-fit this/all columns" and on edge
double-click. When interop is unavailable (bUnit, disposal race) it falls back to a
character estimate so the grid never renders unsized. **[corrected]** the header font is
Segoe UI at the grid's 12 px (`results-grid.css:126-131` sets only the family), not 14 px —
the fallback estimate uses 7 px per character for both; the probe read makes it moot at
runtime.

**Why.** `measureText` is font-accurate and sub-microsecond; selecting candidates in C# keeps
the payload to ≤ 4 strings per column (200 columns → 800 strings), so SC-001/SC-009 hold
without sampling rows away. DOM measurement would force layout on visible cells only.

**Rejected.** DOM `scrollWidth` (layout thrash, invisible rows unmeasured); pure character
estimate (wrong without Consolas); sampling the first 200 rows (may cut a long value further
down); a user setting for the maximum now (spec defers it).

**Files.** `src/AkmlSql.Web/wwwroot/js/akml-results-grid.js` (new; `measureColumns`);
`GridColumnLayout.cs` (candidates, padding, clamp, fallback); `ResultSetGrid.razor`;
`tests/AkmlSql.Web.Tests/Pr247_ResultsGridApplyMessageTests.cs` (`JSInterop.Mode = Loose`);
`tests/AkmlSql.Web.Tests/Grid/GridColumnLayoutTests.cs` (new).

**Risks.** The monospace token has no fallback today (N17) — R67 is a dependency of
measurement accuracy, not only polish.

### R3 — Resize by drag (JS pointer capture) and by keyboard (Blazor)

**Decision.** `akml-results-grid.js` `init(rootEl, dotNetRef)` attaches one delegated
`pointerdown`; on an 8 px `.akml-col-resizer` handle it calls `preventDefault`,
`setPointerCapture`, adds `.akml-resizing` (`user-select:none`), rewrites that track in
`--akml-grid-cols` on `pointermove`, and on `pointerup` invokes `OnColumnResizedFromJs(col, px)`;
.NET stores the width and re-emits identical tracks. A drag under 3 px is a click so the edge
double-click (`@ondblclick` → `AutoFitColumn`) still arrives. Keyboard: with a header cell
focused, Shift+Alt+Left/Right changes the width by 16 px (FR-003) through the same path.
**[corrected]** a cancelled `pointerdown` suppresses the browser's focus change, so the module
calls `rootEl.focus()` explicitly after a drag.

**Why.** Pointer capture and a synchronous `preventDefault` exist only in JS; Blazor
`@onpointermove` round-trips every move and cannot meet "no perceptible delay" (SC-002).
Mirrors the per-feature-module idiom (`akml-connection-manager.js`).

**Rejected.** Blazor pointer events; `<input type=range>` per column; CSS `resize`.

**Files.** `akml-results-grid.js`; `ResultSetGrid.razor` (`IAsyncDisposable`,
`DotNetObjectReference`, `[JSInvokable] OnColumnResizedFromJs`, resizer element, keyboard);
`results-grid.css`.

**Risks.** Alt+Shift(+arrow) is Windows' input-language hotkey on machines with two keyboard
layouts (N22 note from the grid skeptic) — FR-049(c) verification must include it, with the
header menu as the fallback.

### R4 — Selection, focus and editing keys

**Decision.** Selection and focus live in C# in display coordinates: `List<GridRect>` plus a
`HashSet<(r,c)>` of Ctrl-click exclusions, an anchor and a focus cell. Click = cell; Shift-click
= extend; Ctrl-click = add/toggle; row number = row; header = column (the sort glyph is a
separate button, so header click ≠ sort); corner = all; Ctrl+A = all; Shift+Arrow extends;
drag selects a rectangle (`elementFromPoint` → `closest('[data-r][data-c]')`, reported only
when the cell changes). The grid root is the only tab stop (`tabindex=0`, `role=grid`,
`aria-multiselectable`, `aria-activedescendant` → `akml-grid-{set}-{r}-{c}`), the CommandPalette
idiom; the root `@onkeydown` implements Arrow/Home/End/PageUp/PageDown/Ctrl+Home/End/Ctrl+A/
Enter/F2/Escape/Tab/Shift+Tab/Ctrl+0/Ctrl+C/Ctrl+Shift+C/Shift+F10/ContextMenu/Delete and
printable-character-starts-edit; the JS module `preventDefault`s exactly those keys when the
target is not the inline `<input>` (for the input only Tab/Enter/Escape are claimed) and scrolls
the focused cell into view. Editing: single click selects; second click, double-click, Enter,
F2 or a printable character call `BeginEdit`; the input's `@onkeydown` handles Enter (commit,
focus back to root), Escape (`_cancelEdit`, the following `onchange`/`onblur` discard),
Tab/Shift+Tab (commit, move to the next/previous editable cell); `oncontextmenu` is stopped on
the input so its native paste menu survives. Delete/restore buttons stay for pointer users with
`tabindex=-1`; the Delete key on a focused row-number cell toggles delete. Selected cells get
`aria-selected` and `.akml-cell-selected`; High contrast text uses `--akml-text-onaccent`
(FR-091). Cut cells carry a `title` and, when focused, a rendered `.akml-cell-tooltip`
dismissed with Escape (FR-103).

**Why.** `aria-activedescendant` keeps real focus on an element that never unmounts (a focused
virtualised cell would be removed); one tab stop instead of a thousand delete buttons; the
rect + exclusion model gives spreadsheet semantics for Shift/drag while honouring FR-008's
Ctrl-click removal.

**Rejected.** Roving tabindex on cells (breaks under Virtualize); a set of every selected cell
(select-all allocates millions); Blazor `@onpointermove` per cell.

**Files.** `ResultSetGrid.razor`; `src/AkmlSql.Web/Shared/Grid/GridSelection.cs` (new, pure);
`akml-results-grid.js` (key trap list, drag select, `scrollIntoView`, `focusRoot`);
`results-grid.css`; `tests/AkmlSql.Web.Tests/Grid/GridSelectionTests.cs`,
`ResultsGridEditingTests.cs` (new).

**Risks.** Ctrl+Shift+C is the DevTools inspect shortcut in Chromium and Firefox — claimable
while the grid has focus, verify under FR-049(c); Delete with an editable cell focused must
never delete the row.

### R5 — Copy: shared formatters in Core, clipboard with fallback

**Decision.** Move the pure formatters out of `GridCopyAsMenu` (`src/AkmlSql.Shell.Shared/
Productivity/Grid/GridCopyAsMenu.cs:254-562`) into `public static class GridTextFormats`
in `src/AkmlSql.Core/Text/GridTextFormats.cs` (CSV, TSV with an `includeHeader` parameter,
JSON, XML, HTML, INSERT, IN clause, Markdown, `QuoteSqlValue`, escape helpers; the `NULL`
sentinel; an explicit `\r\n` line separator; `IndexOf(char)` instead of LINQ `Contains`
for netstandard2.0), keep one-line delegations in `GridCopyAsMenu` so the SSMS edition is
unchanged, and capture golden strings **before the move** through a throwaway test in
`tests/AkmlSql.Shell.Shared.Tests` (Core.Tests cannot reference the shell), then pin them in
`tests/AkmlSql.Core.Tests/Text/GridTextFormatsTests.cs`. The web builds headers/rows from the
selection in grid order (null → `NULL`, column-cut values in full, engine-cut indicators
verbatim and counted, line breaks → space unless `RetainLineBreaksOnCopy`); Ctrl+C = TSV
without header, Ctrl+Shift+C = TSV with the copied columns' headers. Clipboard write goes
through `copyText(text)` in `akml-ui.js`: `navigator.clipboard.writeText` in a secure context,
else a hidden `<textarea>` + `document.execCommand('copy')`; the result is reported through
the notification service; above ~20 MB the grid warns and copies a bounded prefix.

**Why.** The formatters use only System/Linq/Text; Core multi-targets netstandard2.0 and
net10.0 and is referenced by both Shell.Shared and Web, so "the same text as the SSMS
edition" (FR-031) holds by construction for the structure. The fallback matters because the
installer serves the web over http on a LAN (N14). "Same text" is scoped to the format: SSMS
feeds its grid's own display strings, the web feeds the FR-026 text; both emit the
`INSERT INTO [TableName]` placeholder (the formatter gains an optional table-name argument,
default placeholder, so both editions can be upgraded together later).

**Rejected.** `AkmlSql.Web.Shared` (the shell does not reference it); a re-implementation;
bare `navigator.clipboard` calls (no fallback); chunked clipboard streaming.

**Files.** `src/AkmlSql.Core/Text/GridTextFormats.cs` (new); `GridCopyAsMenu.cs` (delegate);
`src/AkmlSql.Web/Shared/Grid/GridClipboard.cs` (new); `akml-ui.js` (`copyText`);
`ResultSetGrid.razor`; `tests/AkmlSql.Core.Tests/Text/GridTextFormatsTests.cs`,
`tests/AkmlSql.Web.Tests/Grid/GridClipboardTests.cs` (new); History and AI chat switch to
`copyText` (they fail silently on http today).

**Risks.** The SSMS formatters' quirks come along (`NumberStyles.Any` turns `1,000` into
`1000`; `12.50` → `12.5` in JSON; `|` escaped in cells but not headers) — pinned by the
goldens and kept deliberately.

### R6 — Sort: permute `_rowIndexes` only

**Decision.** Sorting permutes `_rowIndexes` (display position → fetched index) and both
render branches iterate it; `RenderRow(set, r)` keeps the fetched index, so edits, deletes,
`KeyCell`, `BuildApplyRequest` and the `results-cell-{r}-{c}` ids stay keyed by fetched row
with no change (FR-016). Per set: `SortColumn`, `SortDirection`; a separate header
`<button aria-label="Sort">` cycles asc → desc → none with `aria-sort`; `ShowResult` clears
it. A pure `GridSortComparer` keyed on `ClrTypeHints[col]`: Int64 → long (decimal fallback),
Double, Decimal, Bool `0/1`, DateTime/DateTimeOffset parsed with `"o"`, Guid →
`System.Data.SqlTypes.SqlGuid` order (available in the browser runtime pack), Binary → decoded
bytes (indicators last), strings → `StringComparer.OrdinalIgnoreCase` (the app runs with
`InvariantGlobalization`), unparsable → ordinal; NULL first ascending; keys parsed once; stable
with original-index tiebreak; sorts the current (edited) value. **[corrected]** after an Apply
the page does not re-run, `BakeCommittedChangesIntoActiveSet` + `RebuildRowIndexes()`
(`ResultsGridComponent.razor:507-519`) reset the permutation while the sort state stays —
the sort is re-applied from the new rows there, and display-coordinate selection is remapped
(sort keeps focus on the same fetched row and remaps selection rects through the permutation).

**Why.** `_rowIndexes` is already the seam; type-aware parsing follows the engine's documented
encodings (`SqlScalarEncoder.cs:25-58`) so no DTO change is needed.

**Rejected.** Sorting `set.Rows` in place (rewrites the security-sensitive flow); server-side
re-execution; culture-aware comparison; sorting display text (forbidden by FR-012).

**Files.** `ResultSetGrid.razor`; `src/AkmlSql.Web/Shared/Grid/GridSortComparer.cs` (new);
`results-grid.css`; `tests/AkmlSql.Web.Tests/Grid/GridSortComparerTests.cs` (new) plus a
component test that edits, sorts, applies and asserts the key cells.

### R7 — Context menu component

**Decision.** One generic `src/AkmlSql.Web/Shared/ContextMenu.razor`: `Items`
(`MenuItem { Label, Shortcut, Disabled, DisabledReason, Children, OnSelect }`), `X`/`Y`,
`OnClose`; transparent fixed scrim, `role=menu` panel flipped into the viewport,
`role=menuitem` buttons with roving tabindex, Up/Down, Right opens a submenu (Copy as ▸),
Left/Escape close, Enter/Space activate, type-ahead, `aria-disabled` + `title` reason; chrome
from tokens; focus lifecycle from `akml-ui.js`. **[corrected]** the grid root installs one JS
`contextmenu` listener that `preventDefault`s unless the target is the inline input and routes
keyboard-originated events (`clientX/Y = 0`, which the Menu key and Shift+F10 produce even when
`keydown` was prevented) to the focused cell's rect; the per-cell Blazor directive is dropped.
Cell menu in FR-017 order: Copy, Copy with headers, Copy as ▸ (CSV / JSON / Markdown table /
INSERT statements), Select all, View value, Set to NULL (editable only; disabled with reason
when `Provenance[c].AllowDBNull` is false or the column is not editable), Save results as CSV…;
plus, after a separator, Delete row / Restore row for editable sets (keyboard users have no
other route once the marker buttons leave the tab order — a deliberate addition to FR-017's
list). Header menu (FR-005): Auto-fit this column, Auto-fit all, Reset column widths, Copy
column name, Sort ascending, Sort descending, Clear sort. The toolbar View menu reuses the
component.

**Why.** No reusable menu exists (the palette is a modal listbox; the refactor picker a
centred overlay); FR-017/FR-005 need pointer-positioned, keyboard-operable menus with a
submenu; one component keeps FR-070/FR-086's one look.

**Rejected.** The palette; native `<menu>`/`<dialog>`; the refactor picker; an npm library.

**Files.** `ContextMenu.razor` (new); `ResultSetGrid.razor`; `akml-results-grid.js`
(`getCellRect`, contextmenu listener); `akml-ui.js` (focus stack); `Pages/Editor.razor`
(`OnSaveResults` → CSV download); `tests/AkmlSql.Web.Tests/Shared/ContextMenuTests.cs` (new).

### R8 — View value and engine-cut indicators

**Decision.** Three cell states, client-side: normal; binary with the full Base64 present
(**[corrected]** shown as `0x` + upper-case hex cut by the column width, per FR-026, not the
old `[binary N bytes]` condensation — the local condensation at
`ResultsGridComponent.razor:673-680` is removed); engine-cut — the raw cell matches
`^\[(text|binary) \d+ (chars|bytes)\]$` (the only in-band forms `ResultSetReader.cs:197,211,221`
emit), rendered muted with `title` "The engine returned only this value's size", copied
verbatim and counted, and View value says the content is not available and suggests a
`LEFT(col, 8000)`-style projection. `CellValueDialog.razor` (new) follows the modal idiom:
header = column + declared type (+ `NULL`/`NOT NULL`), body = size line and the whole value in
a read-only wrapping `<textarea>`, footer = Copy (`copyText`) + Close; opened from the menu, a
key, and the focused-cell tooltip; disabled for NULL.

**Why.** The spec requires honesty about engine-cut values and counting them in copies
(FR-002/FR-009/FR-017); the indicator strings are the only signal without an engine field,
and the Dependencies section limits engine work to its list.

**Rejected.** A structural `CutCells` key (engine scope, recorded as a follow-up); re-querying;
browser `alert()`; an inline expanding cell (breaks the fixed row height).

**Files.** `CellValueDialog.razor` (new); `src/AkmlSql.Web/Shared/Grid/GridCellText.cs`
(new: `IsEngineCutIndicator`, `DescribeSize`); `ResultSetGrid.razor`; `results-grid.css`;
`tests/AkmlSql.Web.Tests/Grid/GridCellTextTests.cs`, `CellValueDialogTests.cs` (new).

### R9 — Per-set grids, pending-edit gating, invalid cells, row glyphs

**Decision.** `ResultSetGrid` (one per result set) owns selection, sort, widths, edits,
inserts, deletes, the inline editor and its own Apply/Discard bar (FR-021); the pane stacks
them. `ResultsGridComponent` remains as the standalone single-set host around `ResultSetGrid`
(the existing bUnit test renders it directly) and `ResultsPaneComponent` stacks `ResultSetGrid`s. `ResultSetGrid` exposes `HasPendingEdits` and `PendingEditCount`, and the page gates
Execute, New query, Open and in-app navigation (Blazor `NavigationLock` with the in-app dialog)
on the sum across sets (FR-015); `ShowResult` no longer resets edits silently — the page asks
first. Validity (FR-014): a blank commit on a character type (`char/nchar/varchar/nvarchar/
text/ntext/xml/sql_variant`) is an empty string; on any other type (including `bit` and
binary) the cell is invalid, marked with `.akml-cell-invalid` + glyph + title "Enter a value or
set NULL"; an unset NOT NULL non-identity column of a new row is invalid; Apply is disabled
while any cell is invalid and the bar says why. Set to NULL stores `null` in the pending edit
(the wire already means `null` = SQL NULL). Edited/new/deleted rows show ✎/＋/✕ glyphs in the
row-number cell as well as the edge-bar colour (FR-016).

**Why.** The grid skeptics showed FR-021/FR-015/FR-014/FR-016 were not covered by any
single-set decision; today's component resets edits on `SelectSet` and `ShowResult`
(`ResultsGridComponent.razor:217-236`). Client-side validity turns a server `FormatException`
and a rolled-back batch into a marked cell.

**Files.** `ResultSetGrid.razor`, `ResultsPaneComponent.razor` (R34); `Pages/Editor.razor`
(gates); `src/AkmlSql.Core/Text/SqlTypeName.cs` (`IsCharacterType`);
`tests/AkmlSql.Web.Tests/Grid/ResultsGridNullAndInvalidTests.cs` (new).

---

## Values, display and export

### R10 — The declared SQL type travels in `ColumnSqlTypes`; the 255 sentinel is fixed

**Decision.** `ResultSetReader` reads `GetColumnSchema()` unconditionally (try/catch, bare name
on failure) and composes the declared type with a Core helper `SqlTypeName.Format` /
`SqlTypeName.Parse` (`src/AkmlSql.Core/Text/SqlTypeName.cs`, new): length types →
`nvarchar(50)` / `nvarchar(max)` (`ColumnSize == int.MaxValue` → max); `decimal(p,s)`;
`datetime2(s)`, `time(s)`, `datetimeoffset(s)`; everything else bare, including three-part
CLR UDT names. **[corrected]** the suffix form is decided by the base type name, never by
"precision ≠ 255" (int reports precision 10, float 15, smalldatetime 16/0, datetime 23/3). In
the same edit, `TryPopulateProvenance` maps 255 to null so `CrudWriteGenerator` never sets
`SqlParameter.Scale = 255` (N2). `numeric` is reported as `decimal` by SqlClient and shown so.

**Why.** FR-026 needs the scale per column and FR-011 the declared type for hover; the string
already exists and is documented in this form; no new key; old/new pairings degrade to bare
names (seven fractional digits).

**Rejected.** New `ColumnScales/Precisions` keys (duplicate of provenance, no hover text);
relying on provenance alone (absent on read-only sets, carries the sentinel); fixing
`ResolveSqlDbType` (changes every Apply's parameter types); engine-side display strings.

**Files.** `SqlTypeName.cs` (new); `src/AkmlSql.Engine/Execution/ResultSetReader.cs:83-87,
102, 303-304`; `src/AkmlSql.Core/Ipc/Messages/ExecuteQueryMessages.cs` (comments);
`tests/AkmlSql.Core.Tests/Text/SqlTypeNameTests.cs` (new);
`tests/AkmlSql.Engine.Tests/Execution/ExecuteQueryIntegrationTests.cs` (declared types; Apply on
a date and a money column succeeds); `CrudWriteGeneratorTests.cs` (null scale leaves the
parameter default); `doc/ipc-api.md` (the execute family is undocumented today).

### R11 — Display formatting in Core

**Decision.** `SqlValueDisplay.Format(wireText, SqlTypeName, clrHint) → DisplayValue
{ Text, IsEngineCut, IsPreview }` (`src/AkmlSql.Core/Text/SqlValueDisplay.cs`, new), called by
the grid for display only; the editor and Apply keep the wire text. Rules: null → `NULL`
(muted); empty string → empty cell with title "(empty string)"; engine indicator → verbatim,
`IsEngineCut`; `date` → first 10 chars; `smalldatetime` → `yyyy-MM-dd HH:mm:ss`; `datetime` →
`.fff`; `datetime2(n)`/`time(n)` → n digits from the 7-digit wire text (`time` arrives as
`"c"`, which omits a zero fraction); `datetimeoffset(n)` → same plus a space and the offset
(SSMS spacing; FR-026 amended 2026-10-08 to show the space; pinned by a test so it is a one-line
change); binary → `0x` + upper-case hex, full to 1,024 bytes then a 512-byte preview with
`IsPreview`; `uniqueidentifier` → upper case; decimal/numeric → wire text (already at declared
scale); money/smallmoney → padded to four decimals from the type name; bit → `1/0`; the rest
as-is. Cells carry a `title` of the first 1,000 characters (FR-002) plus the view-value action.

**Why.** A deterministic transform from the round-trip string plus declared type, unit-testable
without a browser, reusable by copy/export (which must emit "the text the grid shows").

**Rejected.** Formatting in JavaScript; engine-side display strings; changing the encoder;
parsing display text back on commit (deferred — strict ISO input is accepted for this
feature and noted as a follow-up).

**Files.** `SqlValueDisplay.cs`, `EngineCutIndicator.cs` (new); `ResultSetGrid.razor`;
`tests/AkmlSql.Core.Tests/Text/SqlValueDisplayTests.cs`,
`tests/AkmlSql.Web.Tests/Grid/ResultsGridDisplayTests.cs` (new).

### R12 — Copy and Save text paths

**Decision.** Copy (R5) uses display text at full length. Save results as (FR-030): the whole
set as comma- or tab-delimited through `GridTextFormats.Delimited(headers, rows, delimiter)`
(quote on delimiter, `"`, CR or LF with inner quotes doubled; `NULL`; header row; CRLF; UTF-8
with BOM for Excel); file name `<documentName>.csv` / `.tsv` (R64's name, fallback
`SQLQuery1`); a confirmation first when the set is truncated or holds engine-cut cells.
**[corrected]** the "retain line breaks on copy or save" option (FR-052) also governs Save:
off → CR/LF inside values become a space (as SSMS), on → kept and quoted. Add
`downloadText(filename, mime, text, withBom)` to `akml-download.js` beside `downloadBase64`
(no 4/3 Base64 inflation of a 15 MB payload).

**Files.** `GridTextFormats.cs`; `src/AkmlSql.Web/wwwroot/js/akml-download.js`;
`tests/AkmlSql.Web.Tests/js/akml-download.test.mjs` (new); `ResultSetGrid.razor`;
`tests/AkmlSql.Core.Tests/Text/GridTextFormatsTests.cs`.

### R13 — Shared Copy-As formatters

Covered by R5; the goldens-first procedure (capture through `Shell.Shared.Tests`, pin in
`Core.Tests`) protects the SSMS edition, which this feature must not change.

### R14 — NULL, empty and invalid cells

Covered by R9. `Provenance.AllowDBNull` is stored as `== true` (`ResultSetReader.cs:300`), so
an unknown nullability reads as NOT NULL; Set to NULL is disabled only when the set is
editable and the column has a `BaseColumnName`, so exotic sources do not show a false reason.

### R15 — Column type on hover; `ShowColumnTypes` setting

**Decision.** The header shows the declared type on hover and keyboard focus (header cells get
`tabindex=-1` focus via the grid's active descendant and an `aria-describedby` tooltip,
dismissible with Escape — FR-103); the permanent second line renders only when
`ExecutionSettings.ShowColumnTypes` is true (new boolean, default false, same `executionSettings`
key); `(No column name)` for an empty name. The toolbar's `PersistCapsAsync` is removed (N19,
R52) so the new fields are never wiped.

**Files.** `src/AkmlSql.Web/Services/IExecutionSettingsStore.cs`; `ResultSetGrid.razor`;
`tests/AkmlSql.Web.Tests/Grid/ResultsGridHeaderTests.cs` (new).

---

## Execution engine

### R16 — GO splitter over the token stream, inside `TsqlParserService.SplitBatches`

**Decision.** Rewrite `SplitBatches` (`src/AkmlSql.IntelliSense/Parser/TsqlParserService.cs:134-181`)
to use `GetTokenStream` and `TSqlTokenType.Go`, returning `List<SqlBatchSpan> { Index,
StartOffset, EndOffset, StartLine, RepeatCount ≥ 1, Text }` plus a nullable `SeparatorError`.
Rules: a Go token separates; on its line only whitespace, at most one Integer (repeat count)
and a comment may follow — any other token (`GO SELECT 2`, `GO;`) sets `SeparatorError`
("Incorrect syntax was encountered while parsing GO.") and the engine refuses the whole
script, as SSMS does; a block comment before GO on the same line still separates (ScriptDom
emits the token) — stated and tested; `GO 0` and negative counts are separator errors. Batch
text runs verbatim from the line after the previous GO line (StartLine = previous Go line + 1)
to the Go token's offset, so the server's line 1 equals `StartLine`; whitespace-only batches
are skipped; a lexer error (unterminated string) swallows the rest into the last batch. A
cheap pre-check skips lexing when no line starts with `go` (case-insensitive) — lexing costs
~50-70 ms per 10K lines.

**Why.** The lexer already implements SSMS's rule set with Line/Offset per token; full parsing
rejects `GO n`; the regex ignores strings and comments (the spec's explicit edge case); a
second splitter class would be a parallel mechanism (constitution V).

**Files.** `TsqlParserService.cs`; `tests/AkmlSql.Engine.Tests/Parser/TsqlParserServiceTests.cs`
(adapt the seven tests; add strings/comments/`[GO]`/`GO 3 -- c`/case/indent/CRLF/EOF/
consecutive/separator error/unterminated string/start lines/leading blank lines).

### R17 — Per-batch execute loop

**Decision.** `ExecuteQueryHandler.ExecuteOnConnectionAsync` becomes a script loop inside the
one `RunExclusiveAsync` callback (gate held for the whole script; `RegisterQuery` unchanged):
split → for each batch, repeat `RepeatCount` times (SSMS's "Beginning execution loop" /
"Batch execution completed N times." messages when > 1) → a new `SqlCommand` with the clamped
timeout **per command** → drain with `ResultSetReader` sharing one byte budget (R23) → after
each command: `conn.State != Open` → remaining batches `Skipped` + one System message "The
remaining N batch(es) did not run because the connection was closed." and stop; cancellation
→ `Cancelled`, stop; timeout (-2) → batch `TimedOut`, remaining `Skipped`, stop (SSMS
behaviour; X10); any other thrown `SqlException` → its `Errors` recorded for that batch and
continue. **[corrected]** command behaviour is chosen per batch (X3) so `CREATE PROCEDURE …
GO EXEC …` scripts work (N1). Every message and result set carries `BatchIndex`. Top-level
`Status` = Ok when no error anywhere, else Error/TimedOut/Cancelled; `ErrorMessage` = the
first error's text (keeps the old bundle's banner meaningful); `ResultSets` are always returned
(never an `Err` envelope); `ElapsedMs` total plus per-batch. The handler gains the parser
(`EngineHandlerRegistry.cs:339-345` already has it in scope).

**Rejected.** Splitting in the browser (N round trips on a serial socket; a `GO 1000` loop
would flood it); stopping at the first failing batch (contradicts FR-025); continuing after a
timeout (SSMS does not).

**Files.** `src/AkmlSql.Engine/Execution/ExecuteQueryHandler.cs`; `EngineHandlerRegistry.cs:341`;
`ResultSetReader.cs` (budget in/out, batch index, set-boundary callbacks);
`ExecuteQueryIntegrationTests.cs` (three-batch script with a failing middle; `GO 3`; timeout →
TimedOut + Skipped; `CREATE PROCEDURE #p … GO EXEC #p` → Ok; severity-20 → Skipped, skip when
not sysadmin); `tests/AkmlSql.Engine.Tests/Execution/ExecuteOutcomeTests.cs` (new, DB-free
aggregation).

### R18 — Errors as info messages

**Decision.** For the duration of an execute (inside the gate) set
`conn.FireInfoMessageEventOnUserErrors = true` and restore it in `finally`; record every
`SqlError` (from `InfoMessage` and from still-thrown exceptions) as an `ExecuteMessageDto`
(Kind Error when Class ≥ 11 else Info; PRINT is Number 0/Class 0; Number, Severity, State,
Line (0 = none), Procedure, Text, BatchIndex) in stream order. The legacy `Messages string[]`
gets SSMS-formatted lines (`Msg 208, Level 16, State 1, Line 4` + newline + text, with
`Procedure <name>,` before `Line`) so an older bundle still shows them (`.akml-results-message`
is `pre-wrap`).

**Why.** Verified live (N5); `ApplyChangesHandler` depends on exceptions, so the flag must be
scoped to the execute — the gate makes the toggle race-free.

**Files.** `ExecuteQueryHandler.cs`; `ExecuteQueryMessages.cs` (DTO, kind constants);
integration tests (`SELECT 1; SELECT 1/0; SELECT 2` → two sets, one empty, Error 8134 Line 2;
PRINT → Info 0; error inside a real temp proc → `Procedure` populated — the one unverified
case; ApplyChanges after an execute still fails properly → flag restored).

### R19 — Row counts from `StatementCompleted`

**Decision.** Subscribe `cmd.StatementCompleted` per command and append `RowCount` messages
`(N rows affected)` (singular for 1); attribute a SELECT's count to its set (the DONE token
arrives while the set is current, before the next metadata); for a truncated set rewrite the
text to `(N rows returned; more exist)` with N = rows kept, keeping the server count in
`RowCount`; `TotalRowsAffected` (legacy key 6, used by History) = sum of DML counts;
NOCOUNT → no lines; Parse mode does not subscribe.

**Files.** `ExecuteQueryHandler.cs`; `ResultSetReader.cs`; integration tests (ordering with
`ResultSetIndex`; truncated SELECT; NOCOUNT).

### R20 — Additive result shape

**Decision.** Append-only keys per X4: `ExecuteQueryResult` 8 `ExecuteMessageDto[] MessageDetails`,
9 `ExecuteBatchDto[] Batches`, 10 `long CompletedAtUnixMs`, 11 `int MessagesOmitted`,
12 `string? CurrentDatabase`; `ExecuteResultSet` 11 `int BatchIndex`;
`ExecuteMessageDto` 0 Kind (0 Info, 1 RowCount, 2 Error, 3 System), 1 Text, 2 BatchIndex,
3 Number, 4 Severity, 5 State, 6 Line, 7 Procedure, 8 ResultSetIndex (-1), 9 RowCount (-1);
`ExecuteBatchDto` 0 Index, 1 StartLine, 2 StartOffset, 3 EndOffset, 4 Status, 5 ElapsedMs,
6 RepeatCount, 7 ErrorCount; `ExecuteStatus` gains `Skipped = 5` and client-only
`Disconnected = 6`, `NoReply = 7`. No request key for GO (always on) and no `ParseOnly` flag
(R21). Documented in a new `doc/ipc-api.md` section for 212–220.

**Why.** The file's stated contract and the repo's legacy-shape test pattern; an older bundle
skips trailing elements and still gets banner + grids + SSMS-formatted strings; a newer web
on an older engine sees empty details and falls back.

**Files.** `ExecuteQueryMessages.cs`; `ResultSetReader.cs` (BatchIndex);
`tests/AkmlSql.Core.Tests/Ipc/ExecuteQueryMessagesCompatTests.cs` (new; legacy 8-element and
11-element payloads); `doc/ipc-api.md`.

### R21 — Parse as its own message type

**Decision.** `MessageTypes.ExecuteParse = 219` → `ExecuteParseResult = 220` (payload types
`ExecuteQueryRequest`/`ExecuteQueryResult`), registered with `RegisterRaw` to the same handler
in parse mode, gated by capability `execute.v2`. Under the gate: `SET PARSEONLY ON` as its own
command; each batch once with `CommandBehavior.Default` **[corrected]**, the same message
recording, result sets ignored, no `StatementCompleted`; then `SET PARSEONLY OFF` as its own
command — if OFF throws, `conn.Close()` inside the gate so `EnsureOpenAsync` reopens next time
and reports `ConnectionWasReset` through the existing path **[corrected]** (calling the
registry's `Dispose` from inside the gate would cancel the active query's token). Status Ok →
the web prints "Commands completed successfully."; a separator error is reported as in
execute. **[corrected]** `AllMessageTypesInProcessTests` does not fail on a missing request
factory — the plan adds the factory entry and the registration explicitly, and 220 must not be
registered.

**Why.** PARSEONLY is what SSMS's Parse uses and needs no text rewriting (N6); a separate type
is chosen for safety — an older engine that ignored an unknown request key would *execute* the
script the user asked only to parse, whereas an unknown type gets no reply and is hidden by the
capability gate anyway.

**Rejected.** A `ParseOnly` request key; `SET NOEXEC` (binds names; fails on #temp from earlier
batches); `SET FMTONLY` (deprecated, executes code paths); client-side ScriptDom parse (not the
server's verdict).

**Files.** `src/AkmlSql.Core/Ipc/RpcMessage.cs`; `src/AkmlSql.Engine/Capabilities.cs`;
`EngineHandlerRegistry.cs`; `ExecuteQueryHandler.cs`; `AllMessageTypesInProcessTests.cs`;
integration tests (parse of a two-batch script creates nothing; syntax error on line 3 of batch
2 → Error Line 3 with `Batches[1].StartLine`; execute after parse returns rows);
`src/AkmlSql.Web/Services/IQueryExecutionService.cs` (`ParseAsync`).

### R22 — Running state and the reply deadline

**Decision.** `QueryExecutionService.ExecuteAsync` adds a linked token with
`CancelAfter(timeout × max(1, batchCount) + 15 s)`, where `batchCount` (including repeat
counts) comes from the **same** `TsqlParserService.SplitBatches` running in the browser
**[corrected]** (N4; the earlier regex estimate is dropped). **[corrected]** the service must
tell the deadline token from the caller's token before mapping — today every
`OperationCanceledException` maps to `Cancelled` (`IQueryExecutionService.cs:76-79`) and the
bridge cancels the TCS through `ct.Register`; the deadline maps to `NoReply` ("No result was
received within the timeout; the statement may still be running on the engine."), a bridge
`InvalidOperationException` to `Disconnected` ("The engine connection dropped; the statement
may still have run."). `CommandTimeout` is not a wall-clock bound (SqlClient resets it per
network read; truncated sets are drained), so the deadline is a heuristic and the "may still be
running" wording is load-bearing. The engine stamps `CompletedAtUnixMs` (UTC) at the end of the
script; the browser formats `Completion time:` in local time (`yyyy-MM-ddTHH:mm:ss.fffffffK`),
falling back to receipt time when the key is 0. Cancel semantics are unchanged (queued only);
the UI must not present Cancel as interrupting (R41/R65 wording).

**Files.** `IQueryExecutionService.cs`; `ExecuteQueryMessages.cs` (statuses);
`ExecuteQueryHandler.cs` (stamp); `tests/AkmlSql.Web.Tests/Services/QueryExecutionServiceTests.cs`
(new: never-replying fake → NoReply; socket close → Disconnected; batch counting);
`WebHistoryLogic.cs` (NoReply/Disconnected recorded as Error — the statement may have run).

### R23 — One frame budget for the whole reply

**Decision.** The 15 MB `ResultSetReader` budget becomes a single counter across all batches
and messages (each message's UTF-8 bytes, counted twice because the legacy `Messages[]`
duplicates the text); `MaxMessages = 10,000` (`MessagesOmitted` + one System summary);
`MaxResultSets = 1,000` (later sets drained but not kept); `MaxBatchRepeat = 10,000` with a
System message when clamped; sets after exhaustion are kept with 0 rows and `Truncated`.

**Files.** `ResultSetReader.cs`; `ExecuteQueryHandler.cs`; integration tests (`GO 3` over a
wide SELECT with an internal small budget; a 12,000-line PRINT loop → `MessagesOmitted` 2,000).

### R24 — Error-line mapping contract

**Decision.** `documentLine = (selectionStartLine − 1) + (Batches[m.BatchIndex].StartLine − 1) +
m.Line`, valid only when `m.Line > 0` and `m.Procedure` is empty; otherwise no document line
and the click does nothing (FR-023). The engine guarantees `StartLine` is the 1-based line of
the batch text actually sent (verbatim, no trimming). The web captures the selection's start
line at execute time via a new `getSelectionInfo(hostId)` export beside `getSelectedText` in
`akml-editor.js` (**[corrected]** a whitespace-only selection runs the whole document and
must report line 1), wrapped by `EditorComponent.GetSelectionStartLineAsync`, and jumps with
the existing `GotoLineAsync`. A compile error inside a `CREATE PROCEDURE` body carries
`Procedure` and is therefore not navigable — documented as a known limit (SSMS navigates to the
CREATE; a later refinement may map `Procedure` + batch start when the batch begins with it).

**Files.** `akml-editor.js`; `EditorComponent.razor`; `Pages/Editor.razor`;
`src/AkmlSql.Web/Services/ExecuteLineMapper.cs` (new, pure) + tests.

---

## Database selector, session and engine identity

### R25 — ListDatabases by session

**Decision.** Add `Key(1) string? SessionId` to `ListDatabasesRequest`; when set (and the
connection string empty) the handler resolves the session's stored connection string from
`ctx.Sessions`, re-runs alias resolution and `BridgeSqlTargetGuard`, and runs the existing
`sys.databases` projection (`HAS_DBACCESS`). Web: `ISqlConnectionService.ListDatabasesForSessionAsync`
(requires connected + bridge open + capability). The modal's credential path is unchanged.

**Files.** `src/AkmlSql.Core/Ipc/Messages/ListDatabasesMessages.cs`;
`src/AkmlSql.Engine/Handlers/Control/ListDatabasesHandler.cs`; `ISqlConnectionService.cs`;
`tests/AkmlSql.Engine.Tests/Handlers/ListDatabasesHandlerTests.cs` (new, no SQL);
`tests/AkmlSql.Web.Tests/Bridge/SqlConnectionServiceGuardTests.cs`; `doc/ipc-api.md` (94/194).

### R26 — Switch database on the session connection

**Decision.** New typed handler `ChangeDatabaseHandler` (217/218): require a connected session;
reject unsafe identifiers; rewrite the connection string's `Initial Catalog` and run
`BridgeSqlTargetGuard.Check`; under the session gate call the **synchronous**
`conn.ChangeDatabase(db)` inside the gated callback (**[corrected]** SqlClient 7 has no async
override; the base `ChangeDatabaseAsync` just calls it) — same physical connection, so
#temp/SET survive; on success `SessionManager.SetDatabase(sessionId, db, rewrittenConnStr)`
(updates `DatabaseName` + `ConnectionString` **without** the `ConnectionChanged` dispose path
— `SessionConnectionRegistry.GetOrCreate` overwrites the connection's string on the next
call, so a later reopen lands in the new catalog); then **[corrected]** create the new cache
under a claimed populate exactly as `ConnectionChangedHandler` does (`GetOrCreateCache(sid, db)`
+ `TryClaimPopulation` → Phase A → Phase B → `ReleasePopulation`, clearing `PermissionDenied`)
— calling `SchemaRefreshService.Refresh` on a key that does not exist yet races
`SchemaCacheManager.ReloadMissing` into two concurrent Phase A populates and duplicates every
object (N27); reply with `conn.Database` and `ConnectionWasReset`. Web:
`ISqlConnectionService.ChangeDatabaseAsync` runs `ValidateTarget` first, sends 217, sets
`Database`, raises `StateChanged`. A switch on a session whose persistent connection was never
opened opens it first (`EnsureOpenAsync`), so a first-open failure surfaces as the switch error.

**Why.** Re-connecting is forbidden by the spec (password re-prompt, lost #temp/SET; today's
`ConnectionChangedHandler` disposes the session connection at `:63-66`); every IntelliSense/
checksum/refresh path keys the cache by `(sessionId, session.DatabaseName)` (N9), so session
state must move; the claimed-populate sequence already exists in `ConnectionChangedHandler`.

**Rejected.** `ConnectionChanged` with a new catalog; a database field on that notification;
`USE` via ExecuteQuery (history noise, no clean Ok/Error); rewriting `SessionConnection` directly.

**Files.** `RpcMessage.cs`; `src/AkmlSql.Core/Ipc/Messages/ChangeDatabaseMessages.cs` (new);
`src/AkmlSql.Engine/Server/SessionManager.cs` (`SetDatabase`);
`src/AkmlSql.Engine/Handlers/Control/ChangeDatabaseHandler.cs` (new); `EngineHandlerRegistry.cs`;
`ISqlConnectionService.cs` (+ retained `WindowsAuth`);
`tests/AkmlSql.Engine.Tests/Handlers/ChangeDatabaseHandlerTests.cs` (new, no SQL);
integration test (create #temp in tempdb → switch to master → #temp still readable,
`DB_NAME()` = master, session re-keyed; `USE master; RAISERROR` ordering);
`AllMessageTypesInProcessTests.cs`; `doc/ipc-api.md`.

### R27 — Report the current database after each execute

**Decision.** `ExecuteQueryResult.CurrentDatabase` (key 12) = `conn.Database` captured in a
`finally` inside the gated callback after the reader is disposed (the ENVCHANGE token is
consumed by then) and carried on **every** reply, including error and timeout envelopes
(**[corrected]** `USE other; <failing statement>` switches the server-side database and must
still be reported); when it differs from the session's database the handler applies the same
re-key as R26 (`SetDatabase` + a claimed cache populate). Web: `QueryExecutionService` forwards
it to `ISqlConnectionService.ReportCurrentDatabase`, which updates and raises `StateChanged`
only on change; History records `result.CurrentDatabase ?? SqlConn.Database`. Apply safety
**[corrected]**: `CrudWriteGenerator` emits a three-part name only when `BaseCatalog` is set,
so the web fills `ApplyChangesRequest.BaseCatalog` from the set's `BaseCatalog` or, when that
is null, from the reply's `CurrentDatabase` at the time the set was produced — an Apply after a
later `USE` then still writes to the source database. The grid's provenance for a batch after
a mid-batch `USE` was checked against the old cache (read-only grid at worst) — noted,
acceptable. Offline completion picks the most-recently-used snapshot, so a bridge-down session
may complete against the previous database until SchemaSync touches the new key — noted.

**Files.** `ExecuteQueryMessages.cs`; `ExecuteQueryHandler.cs`; `IQueryExecutionService.cs`;
`ISqlConnectionService.cs`; `Pages/Editor.razor`; integration test (`USE master; SELECT DB_NAME()`
from tempdb).

### R28 — Web propagation, the selector component, Schema panel header and refresh

**Decision.** `SqlConnectionService.StateChanged` stays the one event (MainLayout restarts
SchemaSync; Editor retargets the tree; StatusBar relabels; the modal re-renders). New
`src/AkmlSql.Web/Shared/DatabaseSelectorComponent.razor` (server text + `<select>`, lazy list
on open, current name selected even when absent, disabled with the current name when the
capability is absent, not connected, or a query is running; busy state; inline error) placed in
the toolbar's first group (FR-081). `StatusBar.ProbeCacheAsync` probes `(SqlConn.Server,
SqlConn.Database)` instead of the stale host:port/"master" key; the service also retains the
`Login` given at connect (**[corrected]** it exposes only server/database today) for the status
bar's "login when known". The Schema panel header shows
`server · database` and a Refresh action (R40) that sends `SchemaRefreshRequest` (6) **as a
notification** (`IEngineBridge.SendNotificationAsync` — **[corrected]** it has no reply, and a
`SendAsync` would wait forever) and then `ISchemaSync.RefreshAsync()` (new: re-arm the poll,
wait for the engine's phase to settle, fetch Phase A and B unconditionally — the engine
"checksum" is `Phase:objectCount`, so a refresh that changes neither produces no drift — and
raise `ChecksumDrifted` so the tree reloads through its existing path).

**Files.** `ISqlConnectionService.cs`; `DatabaseSelectorComponent.razor` (new);
`Pages/Editor.razor`; `Shared/StatusBar.razor:254-271`; `Shared/SchemaTreeComponent.razor`;
`Services/ISchemaSync.cs`; `tests/AkmlSql.Web.Tests/Bridge/DatabaseSelectorComponentTests.cs`
(new), `StatusIndicatorTests.cs`, `SchemaTreeComponentTests.cs` (FakeSchemaSync gains `RefreshAsync`).

### R29 — Engine host kind on the handshake

**Decision.** `HandshakeResponse` gains `Key(8) string? HostKind` (`"ide"` = started over the
named pipe by SSMS, `"service"` = `WindowsServiceHelpers.IsWindowsService()`, `"console"` =
`--web` run interactively) and `Key(9) string? RunsAs` (`WindowsIdentity.GetCurrent().Name`,
hover text and Diagnostics only). A static `EngineIdentity` set in `EngineHost.RunAsync` /
`RunWebAsync` (the `LanTlsThumbprint` precedent) and read in the three Ok branches. Web:
`IEngineBridge.EngineHostKind` / `EngineRunsAs` as default-interface members (the `ConnectedUrl`
precedent, so the four test fakes do not change); `IProfileStore.EngineStylesGroupName` =
`HostKind == "ide" ? "Shared with SSMS" : "On this engine"`, used by the picker and the Styles
page (FR-063).

**Rejected.** `Environment.UserInteractive`; an installer flag; a capability id (this is a
fact, and FR-063 defines the missing-value behaviour).

**By-design test change.** `FormatStylesSharedEngineTests` launches a sandbox engine with
`--web --config` and asserts the literal "Shared with SSMS"; that engine now reports `console`
and the group reads "On this engine" (N28, R69).

**Files.** `HandshakeResponse.cs`; `src/AkmlSql.Engine/EngineIdentity.cs` (new);
`EngineHost.cs`; `HandshakeHandler.cs`; `IEngineBridge.cs`; `IProfileStore.cs`;
`ProfilePickerComponent.razor:22`; `Pages/Styles.razor:344`; `Pages/Diagnostics` section;
`tests/AkmlSql.Engine.Tests/Handlers/HandshakeHandlerTests.cs`; `tests/AkmlSql.Web.Tests/Styles/*`.

### R30 — Capability and numbering

Per X4: `session.database.v1` gates the selector (an ungated unknown request would hang the
bridge's reply-less `SendAsync`); `execute.v2` gates Parse and the trust in structured
messages. `doc/ipc-api.md` gains 94/194, 212–220 and both capability ids.

### R31 — Engine version display

**Decision.** Keep the wire `EngineVersion` (the Diagnostics bundle wants the raw string);
add `AppVersion.StripBuildMetadata(string)` and a raw `AppVersion.Informational` in Core (the
existing `'+'` logic extracted; **[corrected]** the bundle's web version is already stripped
today, so the raw web string must be exposed to appear there) and use the stripped forms in
the status bar with the raw values on hover; show a mismatch marker when the stripped engine
and web versions differ (FR-083).

### R32 — Saved connections

No store change (no password field, by design); the selector path runs `ValidateTarget`
before any send; saved-connection Connect keeps `ConnectAsync`'s guard and shows the error in
place, never removing the row (FR-057); the engine re-runs `BridgeSqlTargetGuard` on the
stored/rewritten string. The switched database is not written back into the saved record (SSMS
keeps the saved default database).

---

## Workspace layout

### R33 — One CSS grid with named areas

**Decision.** `.akml-editor-grid` + the sibling results pane become one `.akml-workspace` grid:
columns `var(--akml-ws-left) var(--akml-ws-vsplit-l) minmax(var(--akml-ws-editor-min), 1fr)
var(--akml-ws-vsplit-r) var(--akml-ws-right)`, rows `minmax(320px, 1fr) var(--akml-ws-hsplit)
var(--akml-ws-results)`, areas `"schema sl centre sr ai" / "schema sl hsplit sr ai" /
"schema sl results sr ai"` (Schema and AI full height; results under the editor only). Every
size is a CSS variable written inline from .NET layout state (hidden panel → 24 px edge strip
and a 0 splitter; hidden results → 0; maximised results → a modifier class that collapses the
editor row). The results pane host is always mounted (class toggles, never `@if`) so
`ShowResult` and pending edits survive. `.akml-main` loses its 12 px padding on the editor route
(24 px of the SC-003 budget). `SchemaTreeComponent`'s `border-left` becomes `border-right`.

**Why.** Named areas express the T-shaped SSMS layout in one container; splitters adjust one
variable each at frame rate; `minmax(320px, 1fr)` honours the editor's min height and
CodeMirror re-measures through its own ResizeObserver.

**Risk.** Both the grid (> 200 rows) and the schema tree use `Virtualize`, whose
IntersectionObserver may not re-fire when a pane goes from hidden to shown — verify the
re-render on un-hide and call `RefreshDataAsync` if needed; the virtualised body keeps a
bounded-height scroll container.

**Files.** `Pages/Editor.razor` (markup 74-115, styles 285-333, delete `GridColumns` 470-474);
`EditorComponent.razor` (maximised rule); `results-grid.css:8-17` (pane fills its area);
`MainLayout.razor:62-65` (route-scoped padding); `SchemaTreeComponent.razor:101`;
`ProblemsListComponent.razor:69-78`.

### R34 — Results pane shell with the Problems tab

**Decision.** `src/AkmlSql.Web/Shared/ResultsPaneComponent.razor` (new): the `TabStrip` (R58)
with Results / Messages / Problems, a Problems badge (hidden when 0 or analysis is off), the
FR-033 empty state, the running indicator, and three slots filled by the page: the stacked
`ResultSetGrid`s (R9), the Messages view (R20 formatting, click → R24), and the existing
`ProblemsListComponent` with `ShowHeader=false` and `OnVisibleCountChanged` (badge counts the
findings passing the current filters, FR-034; raised only when the count changes, since it fires
from `OnParametersSet`). `ActiveTab` is two-way bound; the page applies FR-020 (Results when
sets and no error, else Messages) and FR-034 (Problems when findings). The grid header's
`results-elapsed`, `results-affected` and `results-tab-messages` ids move to the pane.

**Files.** `ResultsPaneComponent.razor` (new); `ProblemsListComponent.razor`;
`Pages/Editor.razor`; `tests/AkmlSql.Web.Tests/Results/ResultsPaneComponentTests.cs` (new).

### R35 — Splitters, fold-away, constants

**Decision.** Pointer dragging in `akml-workspace.js` (`attachSplitter(el, hostEl, cssVar,
axis, min, max, dotNetRef, id)`: pointer capture, rAF-throttled writes to the workspace's CSS
variable, one `OnSplitterCommitted(id, px)` on release, double-click → `OnSplitterReset`,
`touch-action:none`, a dragging class). Keyboard in Blazor: `SplitterComponent.razor` renders
`role="separator" tabindex="0" aria-orientation aria-valuenow/min/max aria-controls aria-label`
with Arrow ±16 px, Shift+Arrow ±64 px, Home/End → min/max, Enter/Space → collapse/restore;
**[corrected]** the browser defaults are cancelled by a targeted native listener on the
separator (`akml-workspace.js` `trapSeparatorKeys(el)`, the palette's `trapKeys` pattern) that
prevents only Arrow/Home/End/Space/Enter — an unconditional `@onkeydown:preventDefault` would
also cancel Tab and trap keyboard focus (FR-103). Visuals: a 6 px track in
`--akml-border-splitter` (exists in all themes), 12 px hit area, accent on hover/focus.
Fold-away: `observeWorkspace` (ResizeObserver, rAF-debounced) → `OnWorkspaceResized(w, h)`;
**[corrected]** the rule acts on measured widths, not a single threshold: when
`workspaceWidth − visibleSideWidths − splitters < editorMin`, fold the AI panel first, then the
Schema panel (minimums 920 + 180 + 300 + 12 = 1412 px, so at any window below that the two
panels cannot coexist at their minimums); fold state is derived, never persisted (FR-046); edge
strips are `<button>`s with `writing-mode: vertical-rl` and `title="Show Schema (F8)"`.
Constants (planning values per the spec's Assumptions, to be validated against the real
CodeMirror gutter before being frozen in `contracts/ui.md`): editor min width **920 px**
(120 Consolas-13 characters ≈ 858 px + gutter + scrollbar), min height 320 px; Schema min 180 /
default 260; AI min 300 / default 380; results min 120 px / default 38 % of the workspace; edge
strip 24 px. With default sizes the Schema panel folds below ≈ 1210 px, so at 1100 px both side
panels are folded (SC-003).

**Rejected.** Blazor pointer events (jank, no capture); CSS `resize`; `matchMedia` (measures
the viewport, not the editor region); keys in the palette module (single-purpose, every route).

**Files.** `akml-workspace.js` (new; pure `clamp` export for node tests);
`SplitterComponent.razor` (new); `Pages/Editor.razor`;
`tests/AkmlSql.Web.Tests/js/akml-workspace.clamp.test.mjs`,
`tests/AkmlSql.Web.Tests/Workspace/SplitterComponentTests.cs` (new).

### R36 — Layout persistence: a dedicated store with per-field records

**Decision.** New IndexedDB object store `workspaceLayout` (`DB_VERSION` 3 → 4; `StoreNames`
constant), one record per field: `layoutVersion`, `navVisible`, `schemaVisible`,
`schemaWidth`, `aiVisible`, `aiWidth`, `aiTab`, `resultsVisible`, `resultsHeight`,
`resultsMaximised`, `resultsTab`. `IWorkspaceLayoutStore` (`LoadAsync` = list + parse known
keys, ignore unknown, discard all when `layoutVersion` differs — FR-047; `SetAsync(field,
value)` writes exactly that key — FR-045; `ResetAsync` = clear; `Current` + `Changed`). A pure
`WorkspaceLayout.Fit(layout, width, height, limits)` clamps on load and resize and is never
written back; `layoutVersion` is written on the first user change, never on load. Fix
`akml-indexeddb.js` `onversionchange` to `close()`, drop the cached promise **and reopen
without a version argument** (**[corrected]** reopening with the old tab's lower `DB_VERSION`
throws `VersionError`; N26). Users with two app tabs open see the existing "upgrade is blocked"
message once after the update — release note.

**Why.** Per-field keys make "write only the fields the user changed" literally true and
avoid read-merge-write races between tabs; a dedicated store is clearable as a unit (FR-101).

**Rejected.** A JSON blob in `analysisSettings` (whole-record writes); `localStorage`;
debounced whole-layout writes.

**Files.** `akml-indexeddb.js`; `JsIndexedDbAdapter.cs`; `src/AkmlSql.Web/Services/
IWorkspaceLayoutStore.cs` (new); `Program.cs`; `Pages/Editor.razor`;
`tests/AkmlSql.Web.Tests/Workspace/WorkspaceLayoutStoreTests.cs` (new).

### R37 — Page-scoped key capture

**Decision.** Every page-wide shortcut moves from the Blazor `@onkeydown` handler
(`Editor.razor:1159-1186`, which cannot `preventDefault` — Ctrl+S also opens the browser's save
dialog today, and the chord's second stroke Ctrl+F reaches CodeMirror's Find) into
`akml-workspace.js` `initKeys(dotNetRef)`: a document-level capture-phase listener installed
when the editor page mounts and removed on dispose (FR-028 scoping by lifetime), ignoring
`e.repeat` and any time an `[aria-modal="true"]` element is open, testing `e.code`, and for a
claimed key calling `preventDefault` + `stopPropagation` synchronously before
`invokeMethodAsync('OnWorkspaceKey', commandId)`. Table: F5 execute; Ctrl+F5 parse; Ctrl+R
toggle results; F8 toggle Schema; F6 / Shift+F6 cycle panes (editor → results → schema → AI →
toolbar); Ctrl+K then Ctrl+F / Ctrl+L / Ctrl+S (1.5 s chord, handled in JS so the second
stroke never reaches CodeMirror); Ctrl+S save; Ctrl+Shift+S save as; Ctrl+Alt+N new query;
Ctrl+Alt+O open (Ctrl+N/O/T/W/Tab are browser-reserved and not attempted; the alternatives
are shown in menus — FR-049(b)). Ids resolve against the single command table (R38). F6's
cancellability is the weakest claim and is verified per FR-049(c) before release.

**Files.** `akml-workspace.js` (`initKeys`, pure `resolveKey` export);
`Pages/Editor.razor`; `akml-editor.js:589-592` (comment); `tests/AkmlSql.Web.Tests/js/
akml-workspace.keys.test.mjs` (new).

### R38 — One command table for View menu, palette and keys

**Decision.** `Editor.razor` builds `BuildViewActions()`: `view:toggle-nav`, `view:toggle-schema`
(F8), `view:toggle-results` (Ctrl+R), `view:toggle-ai` (replaces `editor:ai`),
`view:maximise-results`, `view:cycle-panes` (F6), `view:reset-layout`, plus `editor:execute`
(F5), `editor:parse` (Ctrl+F5), `editor:new/open/save/saveas`; registers them with
`ICommandRegistry` next to the context actions, renders the same list in a toolbar "View ▾"
menu (the `ContextMenu` component, R7, with check glyphs for visible panels) and dispatches key
ids against it. `CommandAction` gains optional `Func<bool>? IsChecked` and `Func<bool>?
IsEnabled` (the palette renders disabled rows and does not run them — also FR-027's "no Execute
while running").

**Files.** `Pages/Editor.razor`; `src/AkmlSql.Web/Services/ICommandRegistry.cs:47-63`;
`Shared/CommandPalette.razor`; `tests/AkmlSql.Web.Tests/Services/CommandRegistryViewActionsTests.cs`
(new).

### R39 — Analyser findings in the editor via `setDiagnostics`

**Decision.** Export `setDiagnostics(hostId, diags)` / `clearDiagnostics(hostId)` from
`akml-editor.js` (maps `{from,to,severity,message,source}`, clamps to the document, falls back
to the whole line when `to <= from`, dispatches `cm.lint.setDiagnostics`); `lintGutter()` is
already installed and nothing ever fed it. `EditorComponent.SetDiagnosticsAsync(CodeIssueInfo[])`
maps Severity 0-3 → hint/info/warning/error, message `"{RuleId}: {Message}"`; `Editor.razor`
calls it after every successful analyse (all findings; filters are a panel view), clears it on
New/Open and when findings reset. Theme the lint UI with tokens (underlines as
`text-decoration: underline wavy var(--akml-status-*)`, gutter markers via `::before`,
tooltip in panel tokens — the library's colours are hard-coded and injected at runtime under a
generated class, so the overrides must be scoped under `.akml-editor` with equal or higher
specificity). Do **not** install `lintKeymap` (its F8 collides with the Schema panel); expose
next/previous problem through the palette. The same export serves FR-023's execution-error line.

**Files.** `akml-editor.js`; `EditorComponent.razor`; `Pages/Editor.razor`;
`tests/AkmlSql.Web.Tests/js/akml-editor.diagnostics.test.mjs` (new).

### R40 — Schema panel: always present, filter, refresh, icons, connect

**Decision.** Always render `SchemaTreeComponent` in the schema region; when disconnected it
shows "Connect to SQL Server…" raising `OnConnectRequested` (the page wires
`IConnectionManagerController.Open()`); header `server · database` + Refresh (R28) + a search
input filtering `EnumerateKinds` by `ObjectName.Contains(text, OrdinalIgnoreCase)` (parents
kept, matches expanded, empty kinds hidden only while filtering; 150 ms debounce; the
Virtualize branch gets the filtered list); chevron and kind icons through the `Icon` component
(R62) coloured by the existing `--akml-iconbadge-*` tokens (**[corrected]** there is no `key`
token — key columns use `--akml-iconbadge-index`); a loading row while the schema loads
(FR-088). **[corrected]** Refresh means *reload from the server* (FR-093): send
`SchemaRefreshRequest` (6) as a notification, re-arm polling with `ReportEditorActive` (never
called today, so the checksum poll stops after five minutes — the editor also calls it on
activity from now on), then fetch Phase A and B unconditionally and raise `ChecksumDrifted`;
re-fetching the engine's cached snapshot alone would not reload anything.

**Files.** `SchemaTreeComponent.razor`; `ISchemaSync.cs` (`RefreshAsync`); `Pages/Editor.razor`;
`tests/AkmlSql.Web.Tests/Bridge/SchemaTreeComponentTests.cs`.

### R41 — Workspace status service (merged)

**Decision.** `IWorkspaceStatus` singleton (X8): `Phase { Idle, Running, Succeeded, Failed }`,
`StartedAt`, `ElapsedMs`, `RowCount`, `Outcome`, `ErrorMessage`, `CaretLine/Column`,
`DocumentName`, `IsModified`, `Changed`; `Begin()`, `Complete(ExecuteQueryResult)`,
`Fail(message)`, `SetCaret`, `SetDocument`. `Editor.razor`'s run paths (RunSqlAsync, autorun,
apply-refresh) call it instead of writing toolbar status text; the results pane's running
indicator and the status bar read it; the status bar's existing 1 s timer drives the elapsed
counter; the outcome segment is an `aria-live="polite"` region (FR-103). `RowCount` = rows
returned across sets, or rows affected when no set.

**Files.** `src/AkmlSql.Web/Services/IWorkspaceStatus.cs` (new); `Program.cs`;
`Shared/StatusBar.razor`; `Pages/Editor.razor`; `tests/AkmlSql.Web.Tests/Bridge/
StatusIndicatorTests.cs` (registration) + new segment tests.

### R42 — Hiding the navigation bar, editor route only

**Decision.** `MainLayout` injects `IWorkspaceLayoutStore` and `NavigationManager`; the nav is
hidden only when the route is `/` or `/editor` (query string tolerated) and `NavVisible` is
false, replaced by a 24 px horizontal strip "Show navigation bar"; `view:toggle-nav` writes the
field. Other pages always show the nav.

---

## Settings

### R43 — One Settings component, per-section routes, old addresses as aliases

**Decision.** `Pages/Settings.razor` declares `@page "/settings"`, `@page "/settings/{Section}"`
and `@page "/diagnostics"`; a static alias map resolves `Section` to one of `general`,
`editor`, `format`, `queries`, `code-analysis`, `ai-assistance`, `connections`, `schema-cache`,
`diagnostics` (`ai` → ai-assistance; null → general, or diagnostics when the path starts with
`diagnostics`; unknown → general); section clicks navigate to `/settings/{id}`. The old pages
are deleted; `NavMenu` keeps five entries; the palette's navigation entries are generated from
the section catalog; the status bar's "Set up engine" goes to `/settings/connections` and the
engine indicator opens `/settings/diagnostics` (FR-062). Playwright constraint: at
`/settings/ai` only the AI section renders and its active-provider radios stay the first radios
in the DOM (the E2E suites use `GetByRole(Radio).First`).

**Files.** `Pages/Settings.razor` (rewrite); delete `SettingsAi.razor`,
`SchemaCacheSettings.razor`, `Diagnostics.razor`; `NavMenu.razor`; `CommandPalette.razor:489-497`;
`StatusBar.razor:36,55-58`; `AiPanel.razor:23,34`, `AiChatPanel.razor:25` (links);
`tests/AkmlSql.Web.Tests/Settings/SettingsPageRoutingTests.cs` (new).

### R44 — One component per section

**Decision.** `src/AkmlSql.Web/Shared/Settings/{General,Editor,Format,Queries,CodeAnalysis,
AiAssistance,Connections,SchemaCache,Diagnostics}Section.razor`; extraction is mechanical (drop
`@page`, `<PageTitle>`, `<h2>`, duplicated `<style>`; keep `@code` and every label/role text the
E2E suites select); per-page status lines route to the notification service. `ProfilePicker`'s
label becomes "Style" and its delete fallback the store default (FR-054).

### R45 — Shared settings stylesheet and `SettingRow`

**Decision.** `wwwroot/css/components/settings.css` (imported from `app.css`): the Styles
page's list + main layout (**[corrected]** Styles has three columns — list, option-page tree,
main; settings uses list + main), the 900 px single-column breakpoint, nav rows with an inset
3 px `--akml-accent-primary` marker, `.akml-setting` rows (label | control, help spanning).
`Shared/Settings/SettingRow.razor` (Id, Label, Help, Kind, child content, `data-setting`) for
every simple preference; multi-field forms keep their form styles. Fixes the `--akml-accent`
fallback bug by never using it.

### R46 — Section contract for reset, export and import

**Decision.** `ISettingsSection { Id, Title, DescribeResetAsync(), ResetAsync(), ExportAsync(),
ImportAsync(JsonElement) }` with one implementation per exportable section (General, Editor,
Format, Queries, Code analysis, AI assistance, plus Layout and Grid registered by their areas;
**superseded 2026-10-08**: there is no Grid section — the grid options live in Queries)
and `SettingsPortabilityService` aggregating them for Restore all / Export / Import.

### R47 — Store reset methods

**Decision.** Add `ResetAsync` to `IAnalysisSettingsStore` (delete key `current`),
`IExecutionSettingsStore` (delete `executionSettings` only — never clear the shared store —
and drop the session override), `IAiFeatureSettings`, `IThemeService` (**[corrected]** must
bypass `SetAsync`'s "unchanged → return" so a reset while already on System still deletes the
record; also clears the localStorage mirror), the new `IEditorSettingsStore`, and
`IWorkspaceLayoutStore`. AI reset = `Vault.RemoveAsync` per listed provider (keeps the zeroise
path) then clears the active id; Format reset = the store's default id (**[corrected]**
`SetActiveIdAsync` writes `builtin.khamis`, so a `ResetActiveAsync` is added rather than
passing null). Connections has no Restore defaults; Schema cache's is its Clear all; Diagnostics'
resets the level filters. Restore all covers General, Editor, Format, Queries, Code analysis, AI
assistance and Layout (Grid dropped 2026-10-08, see R46) — never connections, pairing tokens, cache, snippets, history, chat
or the document (X10).

### R48 — Export/import envelope

**Decision.** One JSON document `{ format: "akmlsql-web-settings", version: 1, exportedAt,
appVersion, sections: { general, editor, format, queries, codeAnalysis, aiAssistance, layout,
grid } }` with enums as strings; exclusion by construction (the exporter reads only the
preference stores and `IAiKeyVault.ListAsync` configs — never the connections, pairing-token,
key-material, saved-connection, schema, snippet, chat, document or diagnostics stores). Import:
validate `format`; accept any version; each section DTO **and each nested object**
**[corrected]** carries `[JsonExtensionData]` so the "N unknown settings skipped" count is exact;
apply only sections present; report applied/skipped. Download via `downloadText`
(`akmlsql-web-settings-yyyyMMdd.json`); import via `<InputFile accept=".json">` with the 1 MB cap.
`RuleOverrides` keys are exported verbatim (the analyser matches case-insensitively; Undo removes
the key as stored).

### R49 — Imported providers need a key

**Decision.** `IAiKeyVault.UpsertConfigAsync(config)` rewrites name/model/endpoint while
preserving an existing wrapped key, or creates the record with `HasKey = false`; the providers
table shows a "Needs key" badge (`!HasKey && !IsLocal(providerId)`); the active provider is
applied only if it exists after import; the fully-local guard is re-applied.

### R50 — "Enable code analysis" honoured

**Decision.** `AnalyserService.AnalyseAsync` reads the store first and returns an empty
response when disabled (**[corrected]** a null store counts as enabled — `AnalyserServiceTests`
constructs the service without one); `Editor.razor` keeps `_analysisEnabled`, checks it before
calling the service (**[corrected]** otherwise its "No problems." status would lie), passes
`AnalysisEnabled` + `OnEnableAnalysis` to the Problems list, which renders "Code analysis is
off. Turn on" and hides the badge (FR-034).

### R51 — Problems default filters and suppressed rules

**Decision.** `WebAnalysisSettings.ProblemsFilter { ShowInfo, ShowWarning, ShowError }`
(additive; old records deserialise to defaults); `ProblemsListComponent` takes
`DefaultFilter` applied once on initialisation; Settings › Code analysis edits the defaults and
lists every `RuleOverrides` entry whose value is `off` with Undo (other values shown read-only
as severity remaps). Rule titles from the in-process `RuleRegistry` are a planning check.

### R52 — Query defaults vs the session override

**Decision.** Settings › Queries owns the saved `ExecutionSettings` (MaxRows, CommandTimeoutSeconds,
ShowColumnTypes, RetainLineBreaksOnCopy). The toolbar quick control writes an in-memory
`SessionOverride` on the singleton store (`GetEffectiveAsync()` = override ?? saved);
`PersistCapsAsync` is deleted; the editor seeds from the effective value on mount; saving a
default in Settings clears the override (acceptance 3). The two grid options are read from the
saved defaults only.

### R53 — Editor options through CodeMirror compartments

**Decision.** New `IEditorSettingsStore` (`EditorSettings { FontSize = 13, WordWrap = false,
TabSize = 4 }`, key `editorSettings` in the existing `analysisSettings` store — a third key
beside `current` and `executionSettings`, documented). Font size is CSS-only
(`--akml-editor-font-size` on the host, token fallback); word wrap and tab size go through two
CodeMirror `Compartment`s (present in the vendored bundle: `EditorView.lineWrapping`,
`EditorState.tabSize`, `indentUnit`) with a new `setEditorOptions(hostId, opts)` export and an
optional create-time argument. "Reset editor session" says exactly what it clears and confirms
naming the document (R64).

### R54 — Theme before first paint

**Decision.** Mirror the theme to `localStorage['akml.theme']` from `akml-theme.js` when Blazor
applies it (`remember = true` on initialise/set/reset; cleared on reset); convert
`akml-theme-boot.js` into a classic synchronous external script placed right after the
`<link id="akml-theme-css">` in `index.html` (a deferred module can run after first paint;
inline scripts are out under the CSP `script-src 'self'`), reading the mirror (fallback: OS
detection) and setting the existing link's `href` and `data-akml-theme` synchronously.
IndexedDB stays the source of truth. The two files must agree on the theme map (node test).

### R55 — Settings filter across sections

**Decision.** A static `SettingsCatalog` (sections in FR-051 order; one `SettingEntry { Id,
SectionId, Label, Help, Keywords }` per simple preference) and a cascading `SettingsFilter`:
in filter mode every section renders, each `SettingRow` hides itself unless label or help
contains the text (ordinal-ignore-case), sections show their title only with a visible row,
the list greys zero-match sections (`aria-disabled`); clearing restores the open section.
Tables/forms appear only when their section title matches. A consistency test keeps catalog and
rendered `data-setting` ids in step.

### R56 — Multi-tab next-load semantics

**Decision.** No BroadcastChannel. Stores gain `ReloadAsync()` (drop cache, re-read); sections
call it on initialisation and mutate the freshly loaded object (read-modify-write of one field);
loading, filtering and section switches never write; import/reset write once per store.
**[corrected]** `DiagnosticsRingBuffer.RestoreAsync` gets a restored guard (N20) so a hidden
Diagnostics section does not duplicate entries.

### R57 — Confirmations

All destructive settings actions (Restore defaults/all, Remove provider, Clear all cached
schemas — including the palette's `schema:clear` —, Remove engine connection, Reset editor
session) go through `IDialogService` (R59) with `DescribeResetAsync`'s lines as the body.

---

## Shell, documents and polish

### R58 — `TabStrip` component

**Decision.** `Shared/TabStrip.razor`: `Tabs` (`TabItem(Id, Label, Badge, BadgeKind, TestId,
Title)`), `ActiveId` + `ActiveIdChanged`, `AriaLabel`, `Trailing` (AI close, results stats);
`role=tablist` → `role=tab` buttons with `aria-selected`, `aria-controls`, roving tabindex;
Left/Right (wrap) and Home/End move focus **and** activate; Enter/Space activate;
`trapTabStripKeys(el)` in `akml-ui.js` prevents only Arrow/Home/End defaults (the palette's
`trapKeys` idiom); 28 px tall; the active marker is a 2 px bottom border in
`--akml-accent-primary` (no filled background, readable in High contrast); badges are pills
with `data-kind` error/warning colours; badge text updates in place. **[corrected]** `Trailing`
renders as a sibling of the `role=tablist` element (ARIA allows only tabs inside it), and every
existing tab id is preserved — tests select `ai-tab-chat` (E2E) and `preview-mysql` (bUnit).
Replaces the three strips (results, AI dock, Styles preview). `@ref` into an array indexer is
accepted by the Razor compiler, so no child component is needed.

### R59 — Dialog service

**Decision.** `IDialogService` (`ConfirmAsync`, `PromptAsync` with validation, `AlertAsync`;
`DialogRequest { Title, Message, Details, ConfirmLabel, CancelLabel, Destructive }`) with
`Shared/DialogHost.razor` mounted in `MainLayout` after the connection modal (one request at a
time, queued): `.akml-scrim` at z-index 1100 (above the connection manager's 1000), panel
`role="dialog" aria-modal aria-labelledby aria-describedby`, Esc cancels, Cancel focused on open,
the destructive button never the Enter target (the WPF FR-005 safety convention), prompt input
autofocused with inline validation; focus lifecycle from `akml-ui.js` (stash stack); the host
is `position:fixed` so it adds no in-flow child to `MainLayout`'s three-row grid. Migrates the
twelve browser pop-ups (History ×3, Styles ×8 via its two wrappers — whose fail-open "confirm
on JS error" path goes away —, ProfilePicker ×1), consolidates History's hand-rolled Compare
modal onto the same scrim/panel styles, and adds the missing confirmations (AI provider, cache
clear incl. palette, engine connection, saved connection, snippet, diagnostics clear, reset
session, grid Discard, single cache entry, History remove-older/delete). **[corrected]** the
grid's View value needs a scrollable body with a Copy button that `DialogRequest` cannot
express, so `CellValueDialog` (R8) is its own component built on the shared `.akml-scrim` /
`.akml-dialog` styles and the same focus helpers. `StylesReviewFixTests` asserts on JS
`confirm`/`prompt` invocations and must be rewritten against a fake `IDialogService`
registered in the Styles test fixtures (R69).

### R60 — Notification service

**Decision.** `INotificationService` (`Notify(text, kind, duration)`, `Changed`) with
`Shared/NotificationHost.razor` in `MainLayout` between `<main>` and the status bar: a
bottom-right stack (z-index 1050), `role=status aria-live=polite` for info/success,
`role=alert` for errors (which stay until dismissed), 4 s auto-dismiss, Escape/close, at most
three visible, `data-testid="toast"` (never the reserved `error-banner`); the host is
`position:fixed` (no in-flow child in `MainLayout`'s three-row grid). Transient page messages
("Imported …", "Cleared …") move here; execution outcome goes to the status bar (R41), not
toasts; form validation stays inline; **[corrected]** the Format styles page keeps its inline
`styles-status` line (already `role=status`) because five E2E assertions read it by test id
("Saved", "now uses", "Created", "on the engine") — it may additionally raise a toast.

### R61 — One shell stylesheet and page header

**Decision.** `wwwroot/css/components/shell.css` (imported after `results-grid.css`): `.akml-btn`
(+ primary/danger/icon/active/disabled), `.akml-scrim`, `.akml-dialog`, `.akml-field` /
`.akml-field-inline`, inputs, `.akml-hint`, `.akml-page` / `.akml-page-header` (title,
subtitle, spacer, actions), `.akml-table` (sticky head, selected row with
`--akml-text-onaccent` under `[data-akml-theme=high-contrast]`), `.akml-empty`, `.akml-tabs`,
`.akml-icon`; compatibility aliases (`.akml-tool-button`, `.akml-connmgr-btn`) during
migration; delete the 13 per-file `.akml-tool-button` blocks, the three `.akml-overlay` copies,
the two conflicting `.akml-field` definitions, `.akml-profile-button`, `.akml-status-action`
(`RefactorPreviewPanel` keeps the `akml-tool-button` class on its footer buttons because
`RefactorPreviewPanelTests` selects it; the alias covers it).
One page header on History (new), Snippets, Schema cache, Diagnostics, Format styles, Settings.
**[corrected]** fix the four undefined token names (N17): `--akml-accent` →
`--akml-accent-primary`, `--akml-status-error` → `--akml-status-danger`,
`--akml-history-match-highlight` → `--akml-history-matchhighlight`, and promote the
`--akml-surface-editor` alias in `app.css` to a real token in `docs/theme-tokens.json` (regenerated,
both theme folders; WPF never parses the JSON — `ThemePalette.cs` is hand-mirrored — so shared
brand colours are mirrored there as the token system requires, while web-only tokens such as
the dialog scrim and splitter hover need no WPF entry). Razor `<style>` blocks are global and
emitted after the stylesheet, so the migration must delete them, not only add the file.

### R62 — Icon set: Lucide sprite

**Decision.** Vendor Lucide (ISC) the way CodeMirror is vendored: `src/AkmlSql.Web/tools/icons/`
(`package.json` with `lucide-static` pinned, `build-sprite.mjs`, an explicit `icons.json`
allow-list of ~45 names) writes `wwwroot/lib/icons/akml-icons.svg` (`<symbol id="akml-icon-{name}">`,
`stroke="currentColor"`) and a `THIRD-PARTY-NOTICES.txt`; `Shared/Icon.razor` renders
`<svg class="akml-icon" aria-hidden><use href="lib/icons/akml-icons.svg#akml-icon-{Name}"/></svg>`
(or `role=img aria-label` with `Label`); kind colours from the unused `--akml-iconbadge-*`
tokens. Toolbar, palette, status bar, schema tree and grid glyphs switch to it (emoji and text
symbols go). Codicons (the VS look) is rejected because its artwork is CC BY 4.0 (X10).

### R63 — Navigation highlight, five entries

**Decision.** `NavLink` for Snippets, Format styles, History, Settings (`Prefix`); Editor uses
`href=""` with `Match.All` plus a `LocationChanged` check that also marks `/editor` active; the
palette and schema-object navigation canonicalise to `/`; the existing `.akml-nav-link.active`
rule gets `--akml-text-primary` and an HC rule with `--akml-text-onaccent`. bUnit tests use
`BunitNavigationManager` (bUnit 2.9's name) to drive the location.

### R64 — Document model

**Decision.** `EditorSessionRecord` gains `DocumentName`, `IsModified`, `NextDefaultNumber`
(additive JSON, same store); `Services/EditorDocument.cs` (pure: `DefaultName(n)` =
`SQLQuery{n}`, `IsDefaultName`, `SanitizeFileName`, `TitleFor(name, modified, server, db)`);
`EditorSessionKeys` threads the new fields through both existing writers (the whole-record
`SaveAsync` trap) and gains `StartDocumentAsync(store, name, text)` = a fresh record with a **new
SessionKey** (the New/Open boundary; Reset keeps `ClearAsync`). Flows: **New** → confirm if
modified → `StartDocumentAsync(DefaultName(next))` → `SetTextAsync("")` under a
suppress-until-baseline guard (programmatic `setText` fires the change listener); **Open** →
hidden `<InputFile accept=".sql,.txt">` (Snippets idiom) → confirm if modified →
`StreamReader(detectEncodingFromByteOrderMarks: true)` (UTF-16 LE/BE and UTF-8) → refuse above
`DocumentSizeLimit.MaxDocumentSizeChars` naming the 10 MB limit (**[corrected]** the check also
bounds the UTF-8 byte length, since `DocumentChanged` and history records travel in 16 MB
frames) → `StartDocumentAsync(sanitised name, text)` → `EditorComponent.SetTextAsync` with the
page's `_initialText` kept in sync (its `_appliedInitialText` guard otherwise skips an equal
value later); **Save** → prompt for a name only while the name is the default, then download
`{name}.sql` as UTF-8 **with BOM** (X10), clear modified; **Save as** → always prompt, rename
(same SessionKey), download. Modified = text ≠ baseline (set on New/Open/Save; persisted as the
bool). A document header bar above the editor shows `<Icon file-code/> {name}{*}` (FR-073);
`<PageTitle>` renders `orders-audit* - SERVER.db - AKML SQL` (FR-089; no JS). History:
`BuildRecordRequest` gains `tabTitle` = `IsDefaultName(name) ? null : name` — the SSMS shell's
rule (N11) — so user-named documents name their session (the engine upgrades an auto name once,
and a name the user gave in History is kept), while default-named documents read `query-NN` as
SSMS's unsaved tabs do (X10; a literal "SQLQuery1" title would be stored as a user name and
block the later upgrade). New and Open mint a new key, so earlier entries keep their names.
"Open in editor" from History keeps today's behaviour (same key).

**Files.** `IEditorSessionStore.cs`; `EditorSessionKeys.cs`; `EditorDocument.cs` (new);
`WebHistoryLogic.cs`; `Pages/Editor.razor`; `tests/AkmlSql.Web.Tests/Editor/EditorDocumentTests.cs`
(new), `EditorSessionKeyTests.cs`, `History/WebHistoryLogicTests.cs`.

### R65 — Status bar segments and version

**Decision.** Right-hand segments driven by `IWorkspaceStatus` and existing services, left to
right: outcome (`<Icon circle-check|circle-alert|loader-circle/>` + "Query executed
successfully." / "Query completed with errors." / "Executing query…", `role=status aria-live=polite`),
the availability pill (click → `/diagnostics`, which the Settings routes keep as an alias),
connection in the existing `Server/Database` text (**[corrected]** two E2E suites read
`status-connection` and expect that format) plus the login retained by the connection service
(R28), `N rows · M ms` or the elapsed counter while running (its own timer — the existing
countdown timer is set to Infinite whenever no reconnect is pending), caret
`Ln 1, Col 12` (a throttled `selectionSet` branch in `akml-editor.js`'s update listener →
`OnSelectionChangedFromJs`), and `Web 1.26.1007.0613` / `Engine 1.26.1007.0613` through
`AppVersion.StripBuildMetadata` with the raw string on hover and a `triangle-alert` "versions
differ" marker (**[corrected]** the web segment reads the web assembly's informational version,
not Core's; the marker is lit in developer builds because each project stamps its own build
minute unless one `-p:Version` is passed — release builds match; N29). The Cancel control is
shown only while a request is queued, titled honestly. **Superseded 2026-10-08**: the web cannot
tell a queued request from a running one (`IQueryExecutionService.CancelAsync` is
fire-and-forget), so the existing toolbar `execute-cancel` stays and is relabelled "Cancel if not
started" with an honest title instead (tasks T108).

### R66 — Start-up and failure screens

**Decision.** `index.html`'s boot and crash screens get token-styled classes in `app.css` (no
inline styles; the Blazor error UI keeps its id and `.reload` link); the spinner uses `app.css`
keyframes under the reduced-motion rule.

### R67 — Font fallback via the token generator

**Decision.** `docs/theme-tokens.json` typography entries gain a `fallback` field (ignored by
WPF's `ThemeRegistry`) and `scripts/generate-theme-css.ps1` emits a stack per family
(`Consolas, 'Cascadia Mono', 'Courier New', monospace`; `"Segoe UI", system-ui, sans-serif`);
both theme folders are regenerated; the `-CheckOnly` gate stays green.

---

## Test strategy and gates

### R68 — bUnit interop pattern

`JSInterop.Mode = Loose` for component tests plus `SetupModule("./js/<module>.js")` for calls
whose return value matters; assert JS calls through `JSInterop.Invocations`; keep state in C#
so clicks, keydown, dblclick and change drive the components without JS. **[corrected]**
Virtualize and FocusAsync are answered in both modes, and bUnit's Virtualize renders every item
— keep fixtures to a few hundred rows. Shared fakes move to `tests/AkmlSql.Web.Tests/Fakes/`
(`FakeEngineBridge`, `FakeSchemaSync`, a recording `FakeQueryExecutionService` with
`ParseAsync`/`ChangeDatabaseAsync`, `FakeWorkspaceLayoutStore`).

### R69 — Tests that change by design

`StylesReviewFixTests` (four tests asserting JS `prompt`/`confirm` invocations) are rewritten
against a fake `IDialogService` registered in the Styles fixtures, and the two E2E suites
subscribing `page.Dialog` move to the in-app dialog ids; `Pr247_ResultsGridApplyMessageTests`
keeps its ids and its standalone `Render<ResultsGridComponent>` path (the component stays as the
single-set host around `ResultSetGrid`) — **superseded 2026-10-08**: after US2 the test is ported
to `ResultsPaneComponent` and `ResultsGridComponent` is deleted (FR-090; tasks T108); E2E pairing flows navigate to `settings/connections`
and keep `engine-add-*`; `FormatStylesSharedEngineTests` expects "On this engine" from its
`--web` sandbox engine (N28); `SiteScreenshotTour` images change by design. Everything else is the SC-008 regression net: `settings/ai` and
`settings/schema-cache` keep working as section deep-links; the AI form's accessible names stay;
`status-pill` texts and the `status-connection` `Server/Database` text stay; the Styles page's
`styles-status` line stays; toolbar buttons keep their accessible names via `aria-label` when
icons arrive (`Format`, exact, is load-bearing); `ai-button`, `ai-tab-chat`, `preview-mysql`,
`schema-tree-*`, `format-complete`/`analyse-complete` stay, `RefactorPreviewPanel`'s footer
buttons keep the `akml-tool-button` class, and `execute-complete` is added in the same style.

### R70 — Playwright techniques

One E2E suite per user story (`BridgeE2E` + `SkippableFact` over `WebAppFixture`, which gains a
`WithSqlOrSkipAsync` helper pairing the sandboxed Debug engine against `(local)/tempdb`).
**[corrected]** key capture is asserted by `e.defaultPrevented` observed from a bubble-phase
window listener installed with `AddInitScript` plus the `execute-running`/`execute-complete`
side effect (synthetic keys never reload). Drag resize via `Mouse.Move/Down/Move(Steps)/Up` and
bounding boxes; clipboard via `GrantPermissions(clipboard-read/write)` + `readText` with an
`AddInitScript` stub as fallback; layout persistence via reload and `LaunchPersistentContext`;
fold-away via an 1100 × 700 context and the existing overflow script; theme flash via an
`AddInitScript` MutationObserver capturing the first applied `href`; structure via
`ToMatchAriaSnapshotAsync` (pixel snapshots are JS-only). `SiteScreenshotTour` stays the
deliberate refresh path.

### R71 — Engine test split

Pure (no SQL): the splitter cases (in `TsqlParserServiceTests`), message formatting, the line
mapper, outcome aggregation, DTO compatibility (`ExecuteQueryMessagesCompatTests`). Integration
(skip without SQL, same non-parallel collection): per-statement counts in order; error after a
successful SELECT keeps the first set; three GO batches with a failing middle; `GO 2`; NOCOUNT;
`CREATE PROCEDURE … GO EXEC`; Parse creates nothing and leaves the session executable;
ChangeDatabase keeps `#t` and SET state; `USE` reported back; declared types on
`datetime2(3)`/`decimal(10,2)`; Apply on date/money columns; budget across batches. The Engine
suite already takes ~18 min, so live tests stay few and share a connection per class; engine
changes reach the web E2E only after a full publish.

### R72 — SC-008 pass-list comparison

Before the first code change: `dotnet test … --logger "trx;LogFileName=<suite>.before.trx"
--results-directory <local>` for `Web.Tests`, `Web.E2E.Tests` (excluding `SiteScreenshotTour`
by name; the BridgeE2E run on the dev VM with Debug web + engine and SQL up), `Engine.Tests`
(Execution/InProcess/Handlers/Parser), `Core.Tests` (Ipc/Text/Theme), `Site.Tests`
(`ThemeCssSyncTests`). `scripts/compare-test-results.ps1` (PowerShell 5.1, no dependencies)
indexes `UnitTestResult@testName → @outcome` and prints passed-before-not-after,
missing-after, and skipped↔passed transitions; **[corrected]** `.trx` files stay local
(`TestResults/` is already ignored; an Engine trx is ~1.6 MB) and the script's per-test
pass-lists (text) are committed under `specs/041-web-ssms-ux-parity/baseline/tests/` with
`baseline.md` in spec 040's format listing the known reds once.

### R73 — Test-id vocabulary

Kept convention (kebab-case, component-prefixed, interpolated dynamic ids, companion `data-*`,
hidden `*-complete` markers); the reserved list lives in `contracts/testids.md`; existing ids
that tests select are never changed; stacked sets scope `results-cell-{r}-{c}` inside
`results-set-{n}`.

### R74 — Build gates and a CSS token test

`build.ps1` is unchanged. Two unit-level gates join `Web.Tests`: `WebCssTokenTests` scans
`app.css`, `components/*.css` and every razor `<style>` block (comments and `url()` stripped),
mirroring `tests/AkmlSql.Core.Tests/Theme/HardcodedHexScannerTests.cs` (regex + semantic
allow-list), excludes `/spike`, treats `var(--token, #hex)` fallbacks separately, ratchets from
the **real** inventory (**[corrected]**: rgba scrims/shadows in eight components, eight
fallbacks in Styles, one in History) and asserts every used `--akml-*` token exists in
`docs/theme-tokens.json` once the four undefined names are fixed (R61);
`ShortcutCollisionTests` asserts the web shortcut table has no duplicates and none of
CodeMirror's bound keys (the FR-049(d) class of bug, statically). New tokens (scrim, splitter
hover, badge, surface-editor) go into the JSON and are regenerated into both theme folders.

---

## Carried to the user (decided here, easy to reverse)

1. Lucide icons (ISC) rather than Codicons (CC BY 4.0 artwork) — R62.
2. Saved `.sql` files are UTF-8 with BOM — R64.
3. Default-named documents appear in History as `query-NN` (SSMS behaviour); user-named ones
   carry their name — R64.
4. A batch timeout stops the remaining GO batches; `GO SELECT 2` refuses the whole script —
   R16/R17 (SSMS's behaviour; FR-025 amended 2026-10-08 to match).
5. Ctrl+0 (Set to NULL) and Ctrl+Shift+C are claimed only while a grid cell has focus — R4.
6. "Open in editor" from History keeps the current session — R64.
7. The AI reset does not clear chat history; Connections has no Restore defaults; Restore all
   does not clear the schema cache or the log — R47.
8. The cell menu adds Delete row / Restore row after FR-017's list, for keyboard users — R7.
