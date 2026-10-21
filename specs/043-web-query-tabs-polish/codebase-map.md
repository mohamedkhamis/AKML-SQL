# Codebase map: where the web edition assumes one document and one connection

**Branch**: `043-web-query-tabs-polish` | **Date**: 2026-10-21 | Plan input (read-only maps of
the code as it stands on the 042 branch). Paths are relative to the repository root; line numbers
are those of 2026-10-21 and will drift. This file exists so tasks can be written against real
seams; it proposes nothing.

## 1. One engine session, one connection (web)

`ISqlConnectionService` / `SqlConnectionService` (`src/AkmlSql.Web/Services/ISqlConnectionService.cs`,
singleton `Program.cs:88`):

| Member | Line | Sends / does | Per tab |
| --- | --- | --- | --- |
| `SessionId` | 23, minted 210 | `Guid.NewGuid().ToString("N")` once per page load; never persisted | one per tab, persisted (R2) |
| `IsConnected`, `Server`, `Database`, `WindowsAuth`, `Login`, `Kind`, `ServerVersionText`, `TransactionState` | 25-27, 104, 107, 128-134 | set on connect (346-354, 592-600); cleared by `ForgetConnection` (745-758) | per tab |
| `SchemaIdentity` | 140, 248-255 | browser cache key `server (engine host:port)` or `pg://host:port` | per tab's binding (same server → same key, so the browser cache is already shared) |
| `ConnectionGeneration` | 37, 187; `NewTarget` 203-207; bridge reopen 192-200 | bumped on connect, database change, forget, bridge URL change | per tab; the bridge-reopen bump applies to every tab |
| `StateChanged` | 30, 228 | connect, disconnect, database, transaction, bridge URL | fires on tab switch too (R1) |
| `ConnectAsync(ConnectionTarget)` | 161, 314; SQL Server 552-600 | `ValidateTarget` 557 → `TestSqlConnection` 567 → `ConnectionChanged` notification 574-584 (`ConnectionInfo{SessionId, ConnectionString, DatabaseName}`); PostgreSQL 328-338 | sends the tab's id |
| `DisconnectSessionAsync` | 84, 709-743 | needs `session.disconnect.v1` (718); `SessionDisconnect{SessionId}` 727-728 | per tab; tab close adds `Release` (R5) |
| `ChangeDatabaseAsync` | 117, 789-821 | `ChangeDatabase{SessionId, DatabaseName}` 806-807; `ReportCurrentDatabase` 811 | per tab |
| `SendDocumentAsync` | 98, 832-851 | `DocumentChanged{SessionId, FullText}` | per tab |
| `TestAsync`, `ListDatabasesAsync`, credential calls | 62/72/91, 169/172 | no session id | shared |
| `EngineSupportsPostgres`, `CanChangeDatabase` capability part | 143/264, 110/225 | bridge capabilities | shared |
| Guard | `ValidateTarget` 869-904; `IsLoopbackServer` 963-991; `IsRemoteTcpServer` 911 | runs before connect/test/list/change-database; engine repeats it (`BridgeSqlTargetGuard`, `PgTargetGuard`) | unchanged |

Readers that must take a **tab session object** (they can run across a tab switch):

| Site | Lines | Reads |
| --- | --- | --- |
| `Services/IQueryExecutionService.cs` (`QueryExecutionService`, singleton `Program.cs:99`) | 73, 107, 123 (`ExecuteQueryRequest.SessionId`), 134 (`ReportCurrentDatabase`), 139-140 (`ReportTransactionState`), 182 (cancel), 193, 200 (`ApplyChanges.SessionId`) | Kind, IsConnected, SessionId |
| `Pages/Editor.razor` | 1105 (execute), 1109-1113 (history record: Server, Database, SessionKey, Document.Name, Kind), 1130/1141 (`Workspace.KeepResult` with generation), 1180 (parse), 1316 (cancel), 1347 (re-run after Apply), 1594-1597 (format gate + Kind), 999/1660 (analyse) | per tab |
| `Shared/EditorComponent.razor` | 309, 378, 412, 449, 508 (SessionId on completion, quick info, signature, goto, wildcard); 316-318, 373-374, 407-408 (`SendDocumentAsync`) | owning tab |
| `Shared/ResultSetGrid.razor` | 204, 243, 1788, 1948 (`QueryExec.ApplyAsync`, `ConnectionStamp`) | the tab that produced the result |
| `Shared/StatusBar.razor` | 537-539 (`RollbackAsync` runs `ROLLBACK;` through `QueryExec`) | active tab's session, captured before the await |
| `Services/PostgresFormat.cs` 44, 79; `Services/PostgresAnalysis.cs` 46 | `SessionId` on `FormatRequest`, `FormatPreviewRequest`, `CodeAnalysisRequest` | per tab |

Readers that keep reading the facade (active tab's view): `Shared/StatusBar.razor` (93-117, 395-400,
443, 460-486 auto-restore, 515-555), `Shared/DatabaseSelectorComponent.razor` (11-141),
`Shared/SchemaTreeComponent.razor` (parameters 423-424; refresh 447/478 sends
`SchemaRefreshRequest{SessionId}` then `Sync.RefreshAsync()`), `Shared/MainLayout.razor` (109,
163-181: the single `SchemaSync` owner, `StopAsync` then `StartAsync(SessionId, SchemaIdentity ??
Server, Database)` on `StateChanged`), `Shared/Settings/ConnectionsSection.razor` (29-32, 123-124,
164-194), `DiagnosticsSection.razor` (130-134), `Pages/History.razor` (522, 688, 805-853),
`Pages/Snippets.razor` (305-306), `Services/DialectGate.cs:46`, `Services/AiDialect.cs:39-41`,
`Services/IAiSchemaContextProvider.cs:53-81`, `Services/ICompletionService.cs:106` (offline),
`Services/IQuickInfoService.cs:64-73` (offline; also reads the single session record),
`Services/ISignatureHelpService.cs:62`, `Services/ISchemaCacheStore.cs:87-96`
(`ActiveSchemaSnapshot.GetActiveAsync`), `Shared/ConnectionManagerModal.razor` (502, 855-877,
1030-1048 connect target).

Not tied to a session: heavy refactor (`Editor.razor:2059, 2100`, empty id), the T-SQL analyser
(`IAnalyserService.cs:108`), `EnginePostgresRedactor`, `CommandPalette.LoadObjectsAsync`
(454-457), `AiChatPanel`.

`IEngineBridge` (`Services/IEngineBridge.cs`): `SendAsync` 91/357-397 correlates by request id
(`_pendingRequests` 138); `SendNotificationAsync` 106/399-417 (request id 0); the receive loop
419-485 matches pending requests only and drops anything else (no engine→browser notification
routing exists); one socket 135, singleton `Program.cs:80`; capabilities 63/216, read at
`IQueryExecutionService.cs:39/97`, `ISqlConnectionService.cs:223-231/264/697/718`,
`PostgresFormat.cs:111`, `PostgresAnalysis.cs:33`, `AiDialect.cs:97`, `Editor.razor:1159`.

## 2. Engine side

| Item | File:line | Fact |
| --- | --- | --- |
| Web transport is serial | `src/AkmlSql.Engine/Transports/WebSocketTransport.cs:335-409` (handler awaited inline at 379); send at 482 | every frame of a socket is handled in order, including `ExecuteQuery`; `ExecuteCancelHandler.cs:13-18` and `ExecuteQueryHandler.cs:144-145` say so. `NamedPipeTransport.cs:92-114` runs AI types off-loop (the pattern for R3) |
| Per-socket state | `WebSocketTransport.cs:317, 333, 393-399` | source IP and an authenticated flag only; the session id travels in each payload |
| `SessionManager` | `src/AkmlSql.Engine/Server/SessionManager.cs` | `ConcurrentDictionary` 11; `UpdateSession` 15-59; `SetDatabase` 83-89; `GetSession` 91; `KindOf` 101; `Disconnect` 134-139 (keeps the entry and document); `RemoveSession` 141 has **no callers** |
| `ConnectionChangedHandler` | `src/AkmlSql.Engine/Handlers/Control/ConnectionChangedHandler.cs:58-177` (PostgreSQL 188-239) | guard 63; `UpdateSession` 74; disposes the `SessionConnection` only if the string changed 82-85; `InvalidateSession` + `SchedulePrefetch` 90/120; `GetOrCreateCache(sessionId, db)` 96/248; Phase A/B under `TryClaimPopulation` |
| `SessionDisconnectHandler` | `SessionDisconnectHandler.cs:30-42` | `Sessions.Disconnect`, `InvalidateSession`, `SessionConnections.CloseAsync(sessionId, 15 s)` (rolls back) |
| `SessionConnectionRegistry` | `src/AkmlSql.Engine/Execution/SessionConnectionRegistry.cs` | one connection per session id 10-14; `GetOrCreate` 23-49; idle 20 min 18, `EvictIdle` 107-124 only on execute; `CloseAsync` 79-83; no per-client cap |
| Schema cache is per session | `src/AkmlSql.Engine/Schema/SchemaCacheManager.cs:24 (LRU 10), 34-37, 53-62, 127-148` | every caller passes the session id as the first key; disk persistence keyed the same way 165; `ReloadMissing` 39/88-97. Callers: `ConnectionChangedHandler.cs:96/248`, `ChangeDatabaseHandler.cs:188`, `ExecuteQueryHandler.cs:140`, `EngineHandlerRegistry.cs:241/255/267`, `CompletionHandler.cs:57`, `QuickInfoHandler.cs:40`, `SignatureHelpHandler.cs:44`, `WildcardExpansionHandler.cs:37`, `ObjectSearchHandler.cs:52`, `AnalysisHandlers.cs:65/77`, `SchemaHandlers.cs:59` |
| SSMS already fans out sessions | `src/AkmlSql.Shell.Shared/Analysis/DiagnosticTagger.cs:30` (one per buffer), `Commands/ExecuteCurrentStatementCommand.cs:90` (one per command) | the per-session LRU is production behaviour |
| `ChangeDatabase` / `USE` | `ChangeDatabaseHandler.cs:45-115, 183-225`; `ExecuteQueryHandler.cs:183` | `Rekey` / `MoveSession` keep the cache with the session |
| Capabilities | `Capabilities.cs:97-125`; `HandshakeHandler.cs:121/154/182` | `execute.v2`, `session.database.v1`, `session.disconnect.v1`, `dialect.postgresql.v1` |

## 3. One document (web)

| Item | File:line | Fact |
| --- | --- | --- |
| `EditorDocument` (pure) | `Services/EditorDocument.cs` 19-82 | names, sanitising, `TitleFor`, `DecodeFile` (BOM, UTF-8, Windows-1252, 10 MB) |
| `EditorDocumentFlow` (singleton `Program.cs:98`) | `Services/EditorDocumentFlow.cs` | `Name` 81, `IsModified` 83, `NextDefaultNumber` 86, `SessionKey` 89, `Changed` 92; `LoadAsync` 102, `OnTextChanged` 122, `SetTextAsync` 132 (moves the baseline), `AdoptSessionKey` 140, `NewAsync` 144, `OpenAsync` 159, `OpenQueryAsync` 181 (History), `CanReplaceAsync` 213, `StartAsync` 227, `SaveAsync` 247, `SaveAsAsync` 255, `WriteFileAsync` 274, `PersistAsync` 287. New and Open **replace** the document |
| `EditorSessionRecord` | `Services/IEditorSessionStore.cs:30-56` | `DocumentText`, `CursorOffset`, `ActiveProfileId`, `SessionKey`, `SavedAt`, `DocumentName` 49, `IsModified` 52, `NextDefaultNumber` 55 |
| `EditorSessionStore` (singleton `Program.cs:46`) | `IEditorSessionStore.cs:58-170` | key `"current"` 60; 500 ms debounce 61; **one** pending slot 65-66; `SaveAsync` 73; `FlushAsync` 121; `RestoreAsync` 138 (returns the pending copy 143-145); `ClearAsync` 164 |
| `EditorSessionKeys` | `Services/EditorSessionKeys.cs` | `GetOrCreateAsync` 17-34; `ResetAsync` 50 (Settings › Editor `EditorSection.razor:91`); `BuildTextChangeRecord` 70-81; `StartDocumentAsync` 88-103; `SetDocumentTextAsync` 115 (tests only) |
| IndexedDB | `wwwroot/js/akml-indexeddb.js` | `DB_NAME` 10, **`DB_VERSION=4`** 13, `STORES` 15-38, `onupgradeneeded` 51-58 creates missing stores only, `onversionchange` 64-68; node pin `tests/AkmlSql.Web.Tests/js/akml-indexeddb.versionchange.test.mjs:41-48`. `StoreNames` in `Services/JsIndexedDbAdapter.cs:83-117` (`EditorSession` 87, `WorkspaceLayout` 116) |
| `IWorkspaceLayoutStore` | `Services/IWorkspaceLayoutStore.cs` 142-156, 191-257 | `ResultsTab` and `ResultsHeight` are layout-wide |
| `IWorkspaceStatus` (singleton `Program.cs:61`) | `Services/IWorkspaceStatus.cs:67-80, 174-185` | one phase, outcome, caret, `LastResult`/`KeepResult`; `SetDocument` is written but never read |

`Pages/Editor.razor` (2,211 lines): injects 23-47; title 50; `NavigationLock` 54/1397-1411;
toolbar 60-159 (`doc-new` 119, `doc-open` 124-128, `doc-save` 129, `doc-saveas` 133); grid 164 /
CSS 466-508; centre cell 197-211 with `.akml-docbar` 199-206 (`doc-name`, `doc-modified`) and
`EditorComponent @ref="_editor"` 208; results host 224-242 (`ResultsPaneComponent @ref="_resultsPane"`
233-240, always mounted); AI dock `TabStrip` 269-279; per-document state 622-854; `OnInitializedAsync`
856-963 (restore 887, key 898, flow 902-908, commands 961-962); `ExecuteAsync` 1041-1127;
`OnTextChangedAsync` 1488-1501; New/Open/Save 1521-1706; `BuildContextActions` 1417-1486
(`editor:new` 1463); keys 2137-2159; `Dispose` 2174-2210.

`Shared/EditorComponent.razor`: host `id=akml-editor-{Guid}` but fixed `data-testid="sql-editor"`
26/181; `create` 202-247; `InitialText` race guards 190-200, 228-239, 252-264; C# API 620-760
(`SetTextAsync` 644, `ReplaceTextIfUnchangedAsync` 656, `SetDiagnosticsAsync` 690; no focus method);
JS-invokables 278-538. `wwwroot/js/akml-editor.js`: `_instances` map by host id 34 (several
instances already work); `create` 172; compartments 800-804; extensions 806-848; `setText` 979,
`getText` 934, `setDiagnostics` 1273, `dispose` 1323-1338; text change posted on every update
244-250; no exported focus.

`Shared/TabStrip.razor`: parameters 48-67; `TabId`/`PanelId` 70-73; markup 16-44 (`button role=tab`,
`Trailing` beside the tablist); keys 87-109 + `akml-ui.js:162-173`; `TabItem` (`Shared/TabItem.cs:14-21`)
has no close, modified or icon; CSS `wwwroot/css/components/shell.css:261-305` (overflow-x auto only).

`Shared/ResultsPaneComponent.razor`: parameters 143-213; internal `_grids` hold pending edits
215-231; `IdPrefix = "akml-results"` 138; `ResultSetGrid.razor:569` `akml-grid-{SetIndex}`; elapsed
clock reads `IWorkspaceStatus.ElapsedMs` 245-281; `akml-results-grid.js:19` is per root element.

Keys and palette: `Services/WorkspaceKeyMap.cs:27-60` (Ctrl+Alt+N new, Ctrl+Alt+O open, Ctrl+S,
Ctrl+Shift+S / Ctrl+K Ctrl+A save as); `wwwroot/js/akml-workspace.js:15-127` one capture listener,
chords 1.5 s, modal check 42/101, `PANE_SELECTORS` 311-317 fixed ids;
`tests/AkmlSql.Web.Tests/Shell/ShortcutCollisionTests.cs` (CodeMirror keys 16-36, browser-reserved
39-46 = Ctrl+N, Ctrl+T, Ctrl+W, Ctrl+Tab, Ctrl+Shift+Tab; palette keys 49-53; contract keys 99-114);
`Shared/CommandPalette.razor` static commands 506-610 (`sql:connect` 558, `sql:connect-postgresql`
569); `Services/ICommandRegistry.cs` 19-121; keyboard help `wwwroot/js/akml-ui.js:18-63`.

History: `Services/WebHistoryLogic.cs:163-179` (`SessionKey`, `TabTitle` = name unless default);
`Pages/History.razor` 805-836 (`OpenInEditorAsync`, `ReExecuteAsync` → `Document.OpenQueryAsync`,
`/?autorun=1&kind=`); autorun wait `Editor.razor:1251-1302`.

Tests touching the single document: `tests/AkmlSql.Web.Tests/Editor/EditorDocumentFlowTests.cs`,
`EditorDocumentTests.cs`, `EditorSessionKeyTests.cs`, `Editor/SessionRestoreTests.cs`,
`Editor/EditorLifetimeTests.cs`, `Editor/FormatRequestGateTests.cs`, `Results/ResultsPaneComponentTests.cs`,
`Shell/TabStripTests.cs`; E2E `DocumentFlowTests.cs` (New replaces the document :32; `doc-*` ids),
`Harness/SqlSession.cs:41` (`[data-testid='sql-editor'] .cm-content`), `ExecuteVersionAsync` :50
(`execute-complete`); 15 E2E files use `sql-editor`, 4 use `doc-*`, 9 use `results-pane` /
`execute-complete`.

## 4. Typography, tokens, settings

| Item | File:line | Fact |
| --- | --- | --- |
| Tokens | `docs/theme-tokens.json` | groups 5-109 (`Surface.Editor` 16; `Editor.*` 48-51; `Syntax.*` 59-64 with an ignored `italic`); `typography` 122-127 (editor Consolas 13 Normal; no line height; no per-theme values); `$schema` points at a file that does not exist |
| Generator | `scripts/generate-theme-css.ps1` | names 70-79; colours 83-100; groups 117-126 (variant key only); spacing/radius 129-144; typography 146-169 (`-family`, `-size` px, `-weight`); **no line-height branch**; outputs 176-180 (web) and the site copy via `-OutputFolder` |
| Gates | `build.ps1:181-196` (`-CheckOnly` web and site); `tests/AkmlSql.Site.Tests/ThemeCssSyncTests.cs`; `tests/AkmlSql.Web.Tests/Theme/WebCssTokenTests.cs` (literal allowlist per file; every `--akml-*` used must exist; `Spike.razor` excluded) |
| Desktop | `src/AkmlSql.Shell.Shared/Ui/Theme/ThemePalette.cs` (hand-written; light String `#15803D` 126-129, dark Comment `#8A99AF` 229-234, measured 5.0:1), `ThemeTokens.cs:68-70` (Keyword, String, Comment only), `Typography.cs:13-14` | nothing is generated for the desktop |
| Editor CSS | `Shared/EditorComponent.razor:28-161` (host font/size 29-36 with runtime `--akml-editor-font-size`; gutters 42-50; lint 56-78; tooltip 83-88; quick info 124-135 fixed 12 px; peek 156-160 fixed size); completion popup `Pages/Editor.razor:587-618` (fixed `--akml-type-editor-size`) | no CSS for `.cm-content`, `.cm-cursor`, `.cm-selectionBackground`, `.cm-activeLine`, `.cm-matchingBracket`, `.cm-searchMatch` |
| Syntax map | `wwwroot/js/akml-editor.js:188-196` (keyword, string, number, comment italic, function, tablecolumn → `--akml-syntax-*`); fallback `defaultHighlightStyle` 831-832 | no `EditorView.theme`, `contentAttributes` or `spellcheck` anywhere in the file; the bundle exposes `cm.view.EditorView.contentAttributes` and `.theme` already |
| Bundle | `tools/codemirror/cm-entry.js:6-14`; `package.json:8` (`npm run build` → `wwwroot/lib/codemirror/akml-cm.js`, committed); `@codemirror/view` 6.43.5 | CM sets `spellcheck="false" autocorrect="off" autocapitalize="off" writingsuggestions="false" translate="no"` on `.cm-content` |
| Settings | `Services/IEditorSettingsStore.cs` (key `editorSettings` in `analysisSettings` 53; FontSize 13, WordWrap, TabSize 38-40; limits 33-36; `Clamped` 43-48); `Shared/Settings/EditorSection.razor:12-29, 70`; `Services/Settings/SettingsCatalog.cs:53-56`; export `SettingsSections.cs:50-91` (`EditorDto` 85-90; reset text 61 "13 px, off and 4"); path to the editor `EditorComponent.razor:213-220, 762-770` | tests `js/akml-editor.options.test.mjs:12-35`, `Services/EditorSettingsStoreTests.cs`, `Settings/SettingsPortabilityTests.cs:30-34, 91-112`, `SettingsResetTests.cs:20,55,108`, `SettingsSectionsTests.cs:132` |
| Other SQL surfaces | History `<pre class="akml-hist-preview">` 219/267-268 (font 351-353; token colours 355-357 use accent/status/text tokens; spans 919); Styles input textarea 233 (`spellcheck="false"`, font 290), output `<pre class="akml-preview-output">` 237 (13 px fixed, line-height 1.45, 291); Snippets textarea 107 (`spellcheck="false"`, 153/162); chat turn `<pre>` `AiChatPanel.razor:63` (165-171); chat composer textarea 93-97 (no spellcheck attribute, 183-186); `AiPanel.razor:83`; `RefactorPreviewPanel.razor:48-93`; `CellValueDialog.razor:34`; `shell.css:172-182` | |
| Accessibility tooling | `tests/AkmlSql.Web.E2E.Tests/WorkspaceAccessibilityTests.cs:12-81` (three themes via `#setting-theme-control`; 18 token pairs ≥ 4.5:1 with the in-page probe 31-61; no syntax pair); `WorkspaceScreenshotTour.cs` (three themes × 1440×900 and 1100×700 → `specs/041-web-ssms-ux-parity/baseline/after/`; no dialog or popup); `scripts/run-playwright.ps1:79-80` excludes tours unless `-IncludeScreenshots`; `akml-theme-boot.js` (`akml.theme`, OS fallback); no `color-scheme` declaration and no dark-specific selector anywhere (24 for high contrast) | |

## 5. Rename inventory ("SQL Server" meaning "the server")

Rename: `Shared/CommandPalette.razor:559` ("Connect to SQL Server…", `sql:connect`) and `:570`
(`sql:connect-postgresql`, removed); `Shared/ConnectionManagerModal.razor:39` (title per kind);
`Shared/ConnectionPickerComponent.razor:38`; `Services/EngineConnectDiagnosis.cs:53`;
`Shared/StatusBar.razor:555` ("Not connected to SQL Server — click to connect"), `:574` ("connect
to SQL Server for schema-aware IntelliSense"); `Pages/Editor.razor:70` ("Execute needs a SQL Server
connection"); `Shared/SchemaTreeComponent.razor:483`; `Shared/EditorComponent.razor:445`;
`Shared/Settings/ConnectionsSection.razor:24, 38, 42`; `Services/ISqlConnectionService.cs:561`;
same family without the words: `DiagnosticsSection.razor:131`, `StatusBar.razor:101`,
`ISqlConnectionService.cs:624`. Served docs: `doc/deployment.md:316`, `doc/topics/getting-started.md:47`.

Keep (kind, authentication or behaviour of that kind): `ConnectionManagerModal.razor:68, 83, 225,
239, 243-244, 269`; `StatusBar.razor:97, 107, 527`; `History.razor:68`; `Snippets.razor:32`;
`CodeAnalysisSection.razor:161`; `ConnectionsSection.razor:26, 58, 133`; `DiagnosticsSection.razor:134`;
`SchemaCacheSection.razor:62`; `WebHistoryLogic.cs:111`; `SettingsCatalog.cs:69, 76`;
`ISqlConnectionService.cs:895-900`; `AiChatSystemText.cs:12`.

Tests pinned to the strings: `tests/AkmlSql.Web.Tests/Shell/ConnectionManagerModalKindTests.cs:110,
146` (break), `:484` (PostgreSQL hints must not say "SQL Server"); `Bridge/SchemaTreeComponentTests.cs:380`;
E2E `WorkspaceLayoutTests.cs:443`; kept-text pins in `StatusBarKindTests.cs:127-160`,
`ConnectionsSectionKindTests.cs:48-52`, `DiagnosticsSectionPgTests.cs:92`,
`SqlConnectionServiceGuardTests.cs:71-72`, `HistoryPageKindTests.cs:96,136`, the AI dialect tests,
`PostgresWorkspaceTests.cs:174,345`; no test references `sql:connect`.

## 6. Dark-theme surfaces to walk

Pages: Editor (`/`, `/editor`), History, Settings (`/settings`, `/settings/{Section}`, `/diagnostics`:
General, Editor, Format, Queries, CodeAnalysis, AiAssistance, Connections, SchemaCache, Diagnostics),
Snippets, Styles. Dialogs, menus, popups: ConnectionManagerModal, CommandPalette, DialogHost,
CellValueDialog, RefactorInputDialog, RefactorPreviewPanel, ContextMenu, the SchemaTree context menu
(`:210`), the ResultSetGrid tooltip (`:141`), the editor surround picker (`Editor.razor:320`) and
refactor menu (`:351`), the History compare dialog (`History.razor:261`), the Snippets listbox
(`:52`), NotificationHost, CodeMirror's completion, quick info, signature, lint, Find panel and
peek panel. Other shared components: AiChatPanel, AiPanel, AiPrivacyModeBadge, CapabilityNotice,
ConnectionPickerComponent, DatabaseSelectorComponent, MessagesView, NavMenu, ProblemsListComponent,
ProfilePickerComponent, ResultSetGrid, ResultsPaneComponent, SchemaTreeComponent,
SecureContextNotice, SplitterComponent, StatusBar, TabStrip, TlsFingerprintMismatchBanner,
WorkspaceNav, Grid/GridCellEditor, Grid/GridRowBoundary.

Confirmed on the deployed build (dark, connection dialog open, 2026-10-21): text inputs and the
TLS select are token-styled, but a checkbox computed `color: black` on a transparent box, the
dialog's inner scrollbar rendered light, the form scrolled inside the dialog at 1610×860 with the
Password row and the Security group below the fold, and the status bar read "Engine connected —
connect to SQL Server for schema-aware IntelliSense."
