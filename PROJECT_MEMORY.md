# AKML SQL project memory

Last reviewed: **2026-10-05**, Africa/Cairo. Prepared at the owner's request before
web engine enhancements and SSMS screen fixes. Latest implementation follow-up:
**2026-10-06**, section 11.

This is a navigation and engineering reference. The review surveyed the solution,
documentation/spec inventory, build scripts and tests; read the development
guidance, architecture and current feature designs; and traced representative
web, engine and SSMS source paths. It was not a line-by-line audit of every file.
No application build, test suite, deployment, database operation, or live UI
verification was performed during this orientation. Test results below are
explicitly attributed to existing repository records in the orientation sections;
section 11 records later implementation and verification.

## 1. Snapshot and working context

- Solution: [AKML-SQL.slnx](AKML-SQL.slnx), 13 source projects and 13 test projects.
  Shell.Shared is an imported shared-source project; Installer is Inno Setup;
  the interactive UiTests project is outside the solution.
- Branch at review: `040-sqlprompt-ui-parity`; HEAD: `2400a01`
  (2026-10-05, installer icon/header design). These are snapshot facts, not an
  instruction to switch branches.
- SSMS **22** is the only current desktop extension target. VS 2026 support was
  removed on 2026-09-24; many historical documents still describe it and older hosts.
- Three files already had uncommitted changes before this review:
  `src/AkmlSql.Site/wwwroot/docs-metadata.json`, `releases.json`, and
  `update-manifest.json`. They were left untouched. Recheck Git status on future work.
- Primary owner guidance: [CLAUDE.md](CLAUDE.md). Project principles:
  [.specify/memory/constitution.md](.specify/memory/constitution.md).
  Keep changes scoped; reuse existing systems; preserve host isolation and parity
  corpora; deliver code uncommitted unless the user requests Git operations.
- Features have specs, plans, contracts, tasks, and quickstarts under [specs/](specs/).
  Many headers still say Draft even when implementation and verification have
  landed. Read the task ledger, latest progress entry, and source together.
- Keep this reference at the repository root: the product site automatically
  ingests selected Markdown from `doc/`; internal memory should not become public docs.

## 2. Product and project map

AKML SQL is a SQL development assistant with completion, formatting/styles,
analysis, snippets, refactoring, history, execution tools and AI assistance.
SQL Prompt compatibility and familiar interactions are central design goals.

| Area | Source | Role |
|---|---|---|
| SSMS host | [AkmlSql.Ssms22](src/AkmlSql.Ssms22/) | net472, x64, VS SDK 17.14; package startup, registration, SSMS integration |
| Desktop UI | [AkmlSql.Shell.Shared](src/AkmlSql.Shell.Shared/) | `.projitems` compiled into SSMS and shell tests; WPF windows, editor integration, commands, IPC client |
| Engine | [AkmlSql.Engine](src/AkmlSql.Engine/) | net10.0, win-x64, self-contained; SQL connections, schema, request routing, history, execution and engine-side features |
| Shared contracts | [AkmlSql.Core](src/AkmlSql.Core/) | netstandard2.0 + net10.0; settings, IPC DTOs, logging, shared models/helpers |
| Formatting | [AkmlSql.Formatting](src/AkmlSql.Formatting/) | net10.0; formatter pipeline, classic profiles, SQL Prompt document model/layout |
| Completion/parser | [AkmlSql.IntelliSense](src/AkmlSql.IntelliSense/) | net10.0; completion, ScriptDom parser helpers, schema models, lightweight refactoring |
| Analysis | [AkmlSql.Analysis](src/AkmlSql.Analysis/) | net10.0; analysis engine and rule catalog shared by web/engine |
| AI | [AkmlSql.AI](src/AkmlSql.AI/) | net10.0; prompts, providers, privacy/context and streaming infrastructure |
| Browser SQL app | [AkmlSql.Web](src/AkmlSql.Web/) | net10.0 Blazor WASM, CodeMirror 6, local browser services plus optional engine bridge |
| Browser contracts | [AkmlSql.Web.Shared](src/AkmlSql.Web.Shared/) | netstandard2.0 browser contracts |
| Public website | [AkmlSql.Site](src/AkmlSql.Site/) | net10.0 Blazor static SSR; product/download/docs/admin/consent/metrics; separate from the SQL editor app |
| Packaging/utilities | [Installer](src/AkmlSql.Installer/), [Updater](src/AkmlSql.Updater/), [Formatter CLI](src/AkmlSql.Formatter/), [Analyzer CLI](src/AkmlSql.Analyzer/) | Windows installation, update/download flow, command-line tools |

Extracted libraries intentionally retain namespaces such as
`AkmlSql.Engine.Completion` and `AkmlSql.Engine.Parser`. A namespace beginning with
Engine does not mean its source or assembly belongs to the engine executable.

## 3. Architecture and process boundaries

### Desktop and shared engine

SSMS/net472 shell -> PipeRpcClient -> named pipe -> RpcRouter -> engine handlers
-> shared net10 libraries or engine-only database/storage services.

- Shell startup: [AkmlSqlPackage.cs](src/AkmlSql.Ssms22/AkmlSqlPackage.cs).
  Register commands before noncritical initialization; startup failures must not
  make the menu disappear. Theme setup, engine startup, execution capture and
  history restore are wired here.
- Lifecycle/client: [Ipc/](src/AkmlSql.Shell.Shared/Ipc/), especially
  EngineProcessManager, EngineLifecycle, PipeRpcClient and RpcClientAccessor.
- [EngineComposition.cs](src/AkmlSql.Engine/EngineComposition.cs) constructs shared
  process state; [EngineHandlerRegistry.cs](src/AkmlSql.Engine/EngineHandlerRegistry.cs)
  registers typed and raw handlers; [RpcRouter.cs](src/AkmlSql.Engine/RpcRouter.cs)
  dispatches; [RpcContext.cs](src/AkmlSql.Engine/RpcContext.cs) owns cached settings.
- Settings changes use `AnalysisSettingsChanged` to invalidate engine settings.
  Avoid adding a second settings cache or saving behind the existing notification path.
- Engine modes: [Program.cs](src/AkmlSql.Engine/Program.cs) and
  [EngineHost.cs](src/AkmlSql.Engine/EngineHost.cs).
  `--pipe <name> --parent-pid <pid>` runs with the SSMS process; `--web --config <path>`
  runs the WebSocket service without a pipe/parent monitor. Desktop mode can also
  start a bridge when enabled. Both use the same router composition.
- Actual engine publishing has `PublishSingleFile=false` and `PublishTrimmed=false`
  because SqlClient native SNI loading requires it. Older README/diagrams are wrong here.

### Protocol

- Constants are in [Ipc/RpcMessage.cs](src/AkmlSql.Core/Ipc/RpcMessage.cs), alongside
  RpcMessage, **not** a separate MessageTypes.cs. DTOs: [Ipc/Messages/](src/AkmlSql.Core/Ipc/Messages/).
- Named pipe: `akmlsql-engine-{SID}-{PID}`, owner SID allowed, Network SID denied.
  [FrameProtocol.cs](src/AkmlSql.Core/Ipc/FrameProtocol.cs) defines length/checksum
  framing, maximum 16 MB. Envelope keys 0/1/2 are message type/request ID/payload.
  Request ID 0 means notification.
- WebSocket: one binary message contains a MessagePack RpcMessage; it does not use
  the pipe's extra framing. See [Transports/](src/AkmlSql.Engine/Transports/).
- Preserve numeric message IDs and positional DTO keys; extend compatibly.
  Important requests: completion 3; formatting 10–17; schema editor 28; ProfileGet
  34; ProfileRename 35; HistoryRecord/Search/Action 40/41/42; connection test/list
  databases 93/94; handshake 200/201; schema checksum 204/205; phase A/B 208–211;
  execute 212/213. Consult current constants for the rest.
- Desktop document limit is 10 MB; snippet JSON limit is 1 MB. Validate IPC paths
  canonically and preserve async/cancellation behavior.

## 4. Web application enhancement map

Start with [Program.cs](src/AkmlSql.Web/Program.cs), which registers singleton
services. **Most `Services/I*.cs` files contain both interface and implementation**
in the same file; do not assume `CompletionService.cs` or a Services/Impl folder exists.

| Change area | Primary files |
|---|---|
| App layout, connection startup, theme | [Shared/MainLayout.razor](src/AkmlSql.Web/Shared/MainLayout.razor), [Services/IEngineAutoConnect.cs](src/AkmlSql.Web/Services/IEngineAutoConnect.cs), [Services/IThemeService.cs](src/AkmlSql.Web/Services/IThemeService.cs) |
| Editor page and actions | [Pages/Editor.razor](src/AkmlSql.Web/Pages/Editor.razor) (`/`, `/editor`) |
| Editor interop, caret, selection, completion, hover, ghost text | [Shared/EditorComponent.razor](src/AkmlSql.Web/Shared/EditorComponent.razor), [wwwroot/js/akml-editor.js](src/AkmlSql.Web/wwwroot/js/akml-editor.js) |
| Engine connection/handshake/retry | [Services/IEngineBridge.cs](src/AkmlSql.Web/Services/IEngineBridge.cs), [JsBridgeWebSocket.cs](src/AkmlSql.Web/Services/JsBridgeWebSocket.cs), [wwwroot/js/akml-bridge.js](src/AkmlSql.Web/wwwroot/js/akml-bridge.js) |
| SQL connection and connection dialog | [Services/ISqlConnectionService.cs](src/AkmlSql.Web/Services/ISqlConnectionService.cs), [Shared/ConnectionManagerModal.razor](src/AkmlSql.Web/Shared/ConnectionManagerModal.razor), [Services/ISavedSqlConnectionStore.cs](src/AkmlSql.Web/Services/ISavedSqlConnectionStore.cs) |
| Run/cancel/grid changes | [Services/IQueryExecutionService.cs](src/AkmlSql.Web/Services/IQueryExecutionService.cs), [Shared/ResultsGridComponent.razor](src/AkmlSql.Web/Shared/ResultsGridComponent.razor), [Engine/Execution/](src/AkmlSql.Engine/Execution/) |
| Completion and cached schema | [Services/ICompletionService.cs](src/AkmlSql.Web/Services/ICompletionService.cs), [ISchemaSync.cs](src/AkmlSql.Web/Services/ISchemaSync.cs), [ISchemaCacheStore.cs](src/AkmlSql.Web/Services/ISchemaCacheStore.cs), [Shared/SchemaTreeComponent.razor](src/AkmlSql.Web/Shared/SchemaTreeComponent.razor) |
| Local formatting/analysis | [Services/IFormatterService.cs](src/AkmlSql.Web/Services/IFormatterService.cs), [IAnalyserService.cs](src/AkmlSql.Web/Services/IAnalyserService.cs), [Shared/ProblemsListComponent.razor](src/AkmlSql.Web/Shared/ProblemsListComponent.razor) |
| Styles | [Pages/Styles.razor](src/AkmlSql.Web/Pages/Styles.razor), [Services/IProfileStore.cs](src/AkmlSql.Web/Services/IProfileStore.cs), [Shared/ProfilePickerComponent.razor](src/AkmlSql.Web/Shared/ProfilePickerComponent.razor) |
| History | [Pages/History.razor](src/AkmlSql.Web/Pages/History.razor), [Services/IHistoryService.cs](src/AkmlSql.Web/Services/IHistoryService.cs), [WebHistoryLogic.cs](src/AkmlSql.Web/Services/WebHistoryLogic.cs) |
| Snippets/refactoring | [Pages/Snippets.razor](src/AkmlSql.Web/Pages/Snippets.razor), [Services/ISnippetStore.cs](src/AkmlSql.Web/Services/ISnippetStore.cs), [IRefactoringService.cs](src/AkmlSql.Web/Services/IRefactoringService.cs) |
| AI provider setup/chat | [Pages/SettingsAi.razor](src/AkmlSql.Web/Pages/SettingsAi.razor), [Shared/AiChatPanel.razor](src/AkmlSql.Web/Shared/AiChatPanel.razor), [Shared/AiPanel.razor](src/AkmlSql.Web/Shared/AiPanel.razor), [Services/IAiClientFactory.cs](src/AkmlSql.Web/Services/IAiClientFactory.cs), [IAiPromptService.cs](src/AkmlSql.Web/Services/IAiPromptService.cs) |
| Storage/CSS/diagnostics | [wwwroot/js/akml-indexeddb.js](src/AkmlSql.Web/wwwroot/js/akml-indexeddb.js), [wwwroot/css/app.css](src/AkmlSql.Web/wwwroot/css/app.css), [Pages/Diagnostics.razor](src/AkmlSql.Web/Pages/Diagnostics.razor) |

Behavior to preserve:

- Format and analysis run inside WASM using shared libraries; the browser has no
  project reference to the engine executable. Its local path calls services
  directly, rather than routing all work through InProcessTransport.
- One `ISqlConnectionService.SessionId` is used for connection, document, completion
  and execution RPCs. Keep live JS text, caret offsets and engine document updates
  synchronized. Completion bugs can be in JS, the Razor wrapper, the service or
  shared providers; inspect the whole path.
- Completion uses the live bridge when open; offline it combines keywords and the
  most recently used IndexedDB snapshot. This fallback is not full online parser
  parity; multi-database cache selection and alias-context limitations are visible
  in ICompletionService.cs. Signature/quick-info have their own fallback paths.
- Execution uses persistent per-session SQL connections so `#temp`, `SET` and `USE`
  state survives. Cancel uses QueryId. Grid apply uses parameterized writes and a
  transaction; inspect provenance and concurrency rules before modifying it.
- Browser AI has its own direct-to-provider flow, key vault and privacy settings.
  Do not assume desktop agent configuration or provider availability automatically
  transfers to the browser. Provider origin checks/CORS and local-provider setup
  are covered by the M6 docs and IAiClientFactory.
- IndexedDB stores browser settings, snapshots, profiles, sessions, snippets, chat,
  diagnostics and wrapped secrets. Saved SQL connection records omit passwords.
- CodeMirror is vendored locally. Bundle source/build:
  [tools/codemirror/package.json](src/AkmlSql.Web/tools/codemirror/package.json) and
  [cm-entry.js](src/AkmlSql.Web/tools/codemirror/cm-entry.js); runtime shim changes
  generally belong in akml-editor.js, not the generated bundle.
- Theme CSS is generated from [docs/theme-tokens.json](docs/theme-tokens.json).
  Do not hand-edit the generated theme files. Preserve the recent narrow-screen
  `minmax(0, 1fr)` layout fixes and asynchronous disposal guards.

## 5. SSMS screen fixing map

The current contract is [spec 040 UI](specs/040-sqlprompt-ui-parity/contracts/ui.md).
It defines exact labels, stable page keys, menu structure, keyboard behavior and
help routes. The gap plan's original bug descriptions are historical; its top
implementation-status block records the later fixes.

### Options

- [Dialogs/SettingsWindow.cs](src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs)
  owns the window, navigation, search index, working copy, reset/import/export and
  page dispatch. It wraps a WPF Window and implements IOptionsDialog.
- [Dialogs/Pages/](src/AkmlSql.Shell.Shared/Dialogs/Pages/) contains the page builders.
  IPageBuilder supplies Key/Display/Title/HelpTopic/Help/Build; IPageControls owns
  Load/Save/Reset. PageContext supplies settings, PageTheme and RowFactory.
- [RowFactory.cs](src/AkmlSql.Shell.Shared/Dialogs/Pages/RowFactory.cs) owns common
  control rows, descriptions, search registration, parent gating and number fields.
  Fix repeated spacing, enabled/disabled or numeric-field issues here when appropriate.
- [Commands/OptionsCommand.cs](src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs)
  is the shared open/save path. Theme changes reopen with the working copy;
  **Cancel restores the original theme and writes nothing**. SaveAndNotify saves,
  refreshes tab colors/Error List/style state and notifies the engine.
- Page reset must retain hidden data. Do not replace whole settings sections and
  accidentally erase agents/keys, rule overrides, aliases or unrelated style choices.
- Page keys are stable even when labels differ: Behavior=`IntelliSense`,
  Tooltips=`CompletionPolish`, Join conditions=`JoinOptions`,
  Objects & statements=`InsertOptions`, Color=`Tabs & UI`, Styles=`Formatting`.
  Schema Cache/Labs pages were removed from the visible tree.
- Relevant existing test families in [Shell.Shared.Tests](tests/AkmlSql.Shell.Shared.Tests/):
  OptionsThemeSafety, OptionsReopenLoop, OptionsPageReset, OptionsNavStructure,
  OptionsPalette, OptionsDarkCombo, OptionsHoverContrast, RowFactoryGatingAndNumber.

### SQL History

- [HistoryToolWindowControl.cs](src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs)
  renders queries/versions/preview; [HistoryViewModel.cs](src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs)
  handles searching, paging, selections, preview and actions.
- [History/](src/AkmlSql.Shell.Shared/History/) also contains ExecutionCapture,
  DocumentSessionKeys, OpenStateReporter, PendingCloses, ShutdownState,
  HistoryRestoreService, HistorySearchParser and version/preview helpers.
- Engine data path: [HistoryRequestHandler.cs](src/AkmlSql.Engine/History/HistoryRequestHandler.cs)
  -> [HistoryDatabase.cs](src/AkmlSql.Engine/History/HistoryDatabase.cs), with
  QuerySessionStore, QuerySessionNamer and HistoryRetentionService.
- SQLite WAL/FTS5, schema version 3. Storage stays **per execution**; the visible
  grouping is by query session/tab lifetime, not just text hash. Manual names outrank
  filenames, which outrank generated query-NN names.
- Preserve full previews, group-scoped actions, multi-selection after refresh,
  append-only DTOs and HasMore paging. Open ownership includes PID and process start
  time. A cancelled tab close must not record a closed draft. A snapshot needs the
  tab's own session, never another day's row with the same SQLQueryN filename.
- Recent fixes on 2026-10-05: delete all selected groups in one transaction;
  preserve all selected rows across refresh (ID, then session fallback); SQL searches
  match word prefixes; `sql:` excludes names. Read the final baseline follow-ups.
- Shared preview control:
  [Ui/SqlPreview/SqlPreviewView.cs](src/AkmlSql.Shell.Shared/Ui/SqlPreview/SqlPreviewView.cs).
  Used by both History and Format Styles; a visual fix here affects both screens.
- Existing tests include HistorySelectionRefresh, HistoryPaging,
  HistoryPreviewAndActions, HistoryKeyboard, HistorySearchParser,
  HistoryVersionActions, OptionsPageReset and engine History tests.

### Format Styles, menus and other surfaces

- [Formatting/FormatStylesEditorWindow.cs](src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs)
  and [FormatStylesEditorViewModel.cs](src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs)
  own the style list, option tree/controls, change markers and debounced preview.
  Options -> Format -> Styles launches this dedicated editor.
- Preserve readable wrapping/clickable labels, tab-width-correct preview, unsaved
  edit prompts, active marker synchronization, option search and default resets.
  Change markers compare with SQL Prompt defaults; dirty state compares with saved values.
- [Formatting/ActiveStyleCache.cs](src/AkmlSql.Shell.Shared/Formatting/ActiveStyleCache.cs)
  and ActiveStyleMenuCommands connect style changes to menus/status. Runtime menu:
  [Commands/AkmlMenuTable.cs](src/AkmlSql.Shell.Shared/Commands/AkmlMenuTable.cs) plus
  AkmlSqlPackage.EnsureTopLevelMenu. The visible menu has marker `akml-menu-v2`.
  Changing VSCT alone does not change the currently visible SSMS menu.
- Other screen entry points: [Analysis/ManageRulesDialog.cs](src/AkmlSql.Shell.Shared/Analysis/ManageRulesDialog.cs),
  [Dialogs/EditEnvironmentsDialog.cs](src/AkmlSql.Shell.Shared/Dialogs/EditEnvironmentsDialog.cs),
  [Safety/SafetyWarningDialog.cs](src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs),
  [Productivity/CommandPalette/](src/AkmlSql.Shell.Shared/Productivity/CommandPalette/),
  and [Ui/CompletionPopup.xaml.cs](src/AkmlSql.Shell.Shared/Ui/CompletionPopup.xaml.cs).
- Titles use [Core/Config/WindowTitles.cs](src/AkmlSql.Core/Config/WindowTitles.cs):
  `AKML SQL – Name`. F1 uses [HelpBinding.cs](src/AkmlSql.Shell.Shared/Help/HelpBinding.cs)
  and page HelpTopic; routes must exist in the product site's ingested docs.

### WPF conventions

Read [docs/wpf-theming.md](docs/wpf-theming.md) and [Ui/Theme/](src/AkmlSql.Shell.Shared/Ui/Theme/).

- New window/control chrome uses ThemeAwareWindow/ThemeAwareUserControl and
  ThemeTokens via dynamic resource bindings, with ThemeRegistry/HostThemeWatcher.
  Options page builders receive a PageTheme snapshot. Existing special windows
  have their own host wrapper; follow the surrounding implementation.
- Use Typography, Spacing, frozen brushes, themed button/combo helpers, clear focus
  states, automation names and Light/Dark/HighContrast behavior.
- Dialog Owner must resolve to the SSMS/DTE main HWND. Destructive actions do not
  become default buttons; Cancel has IsCancel and initial focus.
- Add new shared-source files to [AkmlSql.Shell.Shared.projitems](src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems).
  Most current dialogs are programmatic WPF; the completion popup is an existing
  XAML exception. Do not introduce a parallel UI framework for a screen fix.

## 6. Formatting and SQL Prompt compatibility

References: [doc/formatting.md](doc/formatting.md),
[spec 039](specs/039-sqlprompt-style-editor/spec.md),
[Formatting/Pipeline/](src/AkmlSql.Formatting/Pipeline/),
[Formatting/SqlPrompt/](src/AkmlSql.Formatting/SqlPrompt/).

- Pipeline: protected regions/SQLCMD preprocessing -> parse -> annotate -> layout
  -> casing/emission -> semantic validation -> idempotency; interactive format
  actions have additional handling. Failed semantic validation returns original SQL.
- Classic AKML profiles use the rule layout. Profiles with a `sqlPrompt` document
  use SqlPromptLayout/SqlPromptPrinter/SqlWriter. Preserve both paths.
- SQL Prompt's JSON document is authoritative; the adjacent AKML option groups are
  a compatibility projection. Preserve unknown fields, IDs, minimal JSON semantics,
  and explicit false switches for the collapse-threshold serialization quirk.
- SqlPromptOptionCatalog/SqlPromptStyleDocument/SqlPromptPreviewSamples supply the
  same 4 categories, 14 pages and 114 schema options plus one documented option to
  web and desktop editors. Correct option behavior in the shared layer.
- Paired browser styles use capability `styles.sqlprompt.v1` and `engine:<name>`
  records; unpaired styles use IndexedDB. Engine summaries must be loaded fully
  before formatting. A failed engine listing retains the last list and reports an
  error; it must not silently overwrite a newer selection or drop unsaved changes.
- ProfileManager uses built-in/user/team sources. Editing a built-in creates an
  override, with Reset restoring it; creating/importing a different style under a
  protected/taken name is refused. CreateOnly distinguishes create from edit.
- Team folder behavior lives in [ProfileManager.TeamFolder.cs](src/AkmlSql.Formatting/Profiles/ProfileManager.TeamFolder.cs).
  Recent changes cache scans for 5 seconds and report inaccessible/read-only cases.
- Format SQL actions default to UseStyle ("As the style says"); retain semantic
  checks for wildcard expansion, qualification and bracket operations.
- Existing formatter goldens are AKML regression baselines, **not proof of exact
  Redgate output parity**. Spec 039 still records calibration against the owner's
  SQL Prompt built-in exports as pending.

## 7. Runtime storage, service context and shared settings

- Desktop settings/logs/history/profiles normally resolve under `%AppData%/AKML SQL`.
  History: `history/sqlhistory.db`; profiles: `profiles`; SQL credentials:
  `sql-credentials.json` (DPAPI). Update cache is under LocalAppData.
- `AKML_APP_DATA_ROOT` redirects the parent root for tests; Constants.AppDataPath
  appends `AKML SQL`. ProfileManager and HistoryDatabase honor it. Use isolated
  roots for engine tests, not the owner's real history/styles.
- Web engine service: `AkmlSqlWebEngine`, installer config normally at
  `%ProgramData%/AKML SQL Web/config.json`; bridge default port 47291. See
  [quickstart-m4](doc/WEB/quickstart-m4.md) and [Installer/](src/AkmlSql.Installer/).
  Browser IndexedDB is separate from engine filesystem storage.
- **Verify account/root before assuming cross-surface sharing.** Specs describe
  shared history/styles; the implementations use the engine process's AppData or
  test override. A LocalSystem web service and a user-owned SSMS engine need not
  resolve the same physical folder. RunWebAsync reads explicit bridge config, while
  EngineComposition's SettingsLoader is ConfigManager.Load. Trace those separately
  when diagnosing web settings or shared-state behavior; the review did not test
  the installed machine's account/config/storage arrangement.
- LAN bridge requires TLS and PIN/bearer handshake. Current WebSocketTransport
  enforces authentication before data RPCs and checks localhost browser origins.
- [BridgeSqlTargetGuard.cs](src/AkmlSql.Engine/Pairing/BridgeSqlTargetGuard.cs) permits
  remote SQL authentication over TCP, but refuses remote use of ambient service
  identity and remote named-pipe/UNC authentication. Old browser-service comments
  claiming all remote SQL is refused or no engine guard exists are stale.
- Desktop multi-agent AI: AiAgentResolver normalizes/migrates settings; agent list
  is authoritative and flat legacy fields mirror the active agent. AiHandlerBase
  resolves/projects the actual feature agent before checking consent. See spec 037.

## 8. Builds and meaningful verification

- Full solution/SSMS shell builds require **full Visual Studio MSBuild**, not
  dotnet build. Restore first, then Build. The old solution-wide CTO collision was
  fixed; full solution builds are supported. See CLAUDE.md's current build gotchas.
- [build.ps1](build.ps1) discovers MSBuild through a preferred path or vswhere,
  runs theme gates, builds/publishes, runs selected suites, and makes the installer.
  It is not a read-only validation command: it cleans outputs and regenerates site
  metadata. DeploySite also deploys. Do not run a release script just to inspect UI.
- Engine/web/shared libraries use dotnet. Installer uses Inno Setup 7. Web publishing
  must not receive `-r win-x64`; engine publishing does. Clean verified publish
  directories before release packaging to avoid accumulated fingerprinted bundles.
- SDKs observed: 10.0.302, 10.0.401, 11.0.100-rc.1.26425.128. Project targets remain
  net10.0/net472/netstandard2.0. Recheck installed tools before future builds.
- Build version is generated as `1.YY.MMDD.HHmm` using a fixed UTC+2 formula in
  build.ps1 and [src/Directory.Build.props](src/Directory.Build.props); README's
  1.0.0 is not the actual per-build identity. This formula is separate from the
  user's Africa/Cairo timezone for communication.

| Change | Tests/checks to start with |
|---|---|
| Browser components/services | [AkmlSql.Web.Tests](tests/AkmlSql.Web.Tests/) (xunit/bUnit), relevant [Web.E2E.Tests](tests/AkmlSql.Web.E2E.Tests/) Playwright cases |
| Engine handlers/history/execution | [AkmlSql.Engine.Tests](tests/AkmlSql.Engine.Tests/), relevant Core DTO tests |
| SSMS screens/state | [AkmlSql.Shell.Shared.Tests](tests/AkmlSql.Shell.Shared.Tests/) (net472, WPF STA, imports actual shared sources), full MSBuild shell build |
| Formatter/style changes | [AkmlSql.Formatting.Tests](tests/AkmlSql.Formatting.Tests/), style-store/editor tests on affected surfaces; preserve [format-parity](tests/format-parity/) goldens |
| Completion | Engine Completion tests and [CorpusGateTests.cs](tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs); shared IntelliSense tests; editor keystroke E2E as needed |
| Theme changes | [audit-wpf-theme.ps1](scripts/audit-wpf-theme.ps1), [generate-theme-css.ps1](scripts/generate-theme-css.ps1) -CheckOnly for Web and Site outputs; Light/Dark/HighContrast and focus review |
| Real SSMS behavior | [AkmlSql.UiTests README](tests/AkmlSql.UiTests/README.md), spec 040 quickstart and screenshot tour against the deployed build |

Important test pitfalls:

1. WebAppFixture launches **Debug** with `--no-build --no-launch-profile` at port
   5000. Build AkmlSql.Web Debug before browser E2E; Release alone can test stale code.
2. FormatStylesSharedEngineTests additionally needs a Debug engine build and starts
   that executable with isolated AKML_APP_DATA_ROOT. Inspect the fixture before use.
3. UiTests needs an interactive desktop and tests **installed extension DLLs**,
   not just the checkout. Screenshot tours use Northwind sample data. Minimized or
   disconnected desktop sessions can produce empty/black captures.
4. Installer smoke tests can run the real installer on admin/IIS machines. Keep
   routine runs to the intended unit tests unless installation is the actual task.
5. Never silently rebaseline golden/corpus output. Existing documents report about
   977 formatter goldens and a roughly 97.5% completion ratchet; current fixtures
   and gate code determine exact counts (some README counts disagree).

### Recorded baseline, not rerun during this review

[Spec 040 baseline](specs/040-sqlprompt-ui-parity/baseline.md), final run 2026-10-04:
Shell 819 passed; Engine 1,990; Formatting 1,515; IntelliSense 25; Site 877.
Core: 1,092 passed, 1 failed, 3 skipped. The recorded Core failure is
`ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only`
(expects 7 keys; response has 9 since spec 039).

The setup baseline records 42 existing Web formatter-corpus failures. Reproduce
relevant failures before classifying a new run; these figures are historical, not
a waiver for newly failing tests. Timing tests can vary under load. The baseline
also distinguishes Pass, Check and Waived manual cases; it is not an all-pass claim.
Latest History follow-up verification is dated 2026-10-05 in the same file.

## 9. Current follow-ups and documentation discrepancies

Follow-ups from current spec 040 tasks/progress (do not implement without scope):

- Missing registered commands remain off the menu: Text to SQL, AI Optimize,
  AI Index Analysis, Generate CRUD Procedures, Find in Results Grid and Split Table.
- VSCT top-level menu remains invisible; runtime DTE menu is the active surface.
- Format SQL actions do not implement SQL Prompt's AS-keyword/column-alias options.
- History's Record failed executions and Encrypt at rest controls remain hidden.
- Grouped WPF lists expose incomplete tree-walk UI Automation; ItemContainer access
  is needed. Screen-reader implications are not established by the recorded runs.
- ConfigManager.Save logs/swallows transient I/O failure; an existing rule-setting
  test has reported intermittent failure. Do not assume a saved UI state proves disk success.
- Schema caches remain per session/tab; current fixes update LRU on use and reload
  evicted active sessions. A shared server/database cache would be a separate change.
- Spec 039's exact SQL Prompt calibration remains pending. Spec 032 retains live
  campaign/performance follow-ups; inspect its task ledger when working on completion.

Documentation cautions:

- README and early architecture/WEB chapters have obsolete host lists, engine
  single-file/trimming claims, build prohibitions, ports, default styles and milestone
  status. Newer source and verification records establish current behavior.
- Early WEB plans say no shared state and per-user web config; actual service config
  is machine-wide, later features add engine-backed history/styles, and account/root
  resolution still matters (section 7).
- Some quickstarts describe unimplemented schema handlers even though phase/checksum
  handlers now exist and are registered. Read current EngineHandlerRegistry.
- InProcessTransport exists, but Web calls shared libraries through its local services.
- CLAUDE.md's spec 037 polish list is stale relative to its current tasks.md, which
  has all 108 tasks checked. Spec 040 similarly has all 198 tasks checked plus an
  explicit Deferred section; checked tasks do not erase that section.
- The constitution still mentions removed VS support and pipe-only networking.
  Existing WebSocket functionality is documented/implemented in later web specs;
  preserve its explicit authentication/transport design when enhancing it.
- The older UI gap file 10 is superseded for Options/History/styles by file 11 and
  spec 040; do not reintroduce an old design based on its original gap table.

## 10. Reading map for subsequent work

| Topic | Documents |
|---|---|
| Orientation/conventions | [CLAUDE.md](CLAUDE.md), [architecture](doc/architecture.md), [constitution](.specify/memory/constitution.md) |
| IPC/configuration | [ipc-api](doc/ipc-api.md), [configuration](doc/configuration.md), current Core DTOs/AppSettings |
| Recent implementation state | [progress](doc/progress.md), especially specs 030–040 and September/October follow-ups |
| Current SSMS UI behavior | [040 spec](specs/040-sqlprompt-ui-parity/spec.md), [plan](specs/040-sqlprompt-ui-parity/plan.md), [UI contract](specs/040-sqlprompt-ui-parity/contracts/ui.md), [IPC contract](specs/040-sqlprompt-ui-parity/contracts/ipc.md), [quickstart](specs/040-sqlprompt-ui-parity/quickstart.md), [baseline](specs/040-sqlprompt-ui-parity/baseline.md) |
| UI rationale/reference comparisons | [gap plan 11](doc/_Prompt-Gap/11-UI-UX-Plan-Options-History-Styles.md), [gap index](doc/_Prompt-Gap/00-INDEX-and-Questions.md), reference images in that folder |
| WPF theme system | [wpf-theming](docs/wpf-theming.md), [016 contracts](specs/016-wpf-theme-refresh/contracts/), [theme tokens](docs/theme-tokens.json) |
| Web architecture/history | [WEB index](doc/WEB/00-INDEX.md), specs 021–028, [030](specs/030-sqlprompt-parity-closure/), [web history design](docs/superpowers/specs/2026-06-28-web-sql-history-design.md), [session grouping design](docs/superpowers/specs/2026-08-12-history-session-grouping-design.md) |
| Bridge installation/security | [quickstart-m3](doc/WEB/quickstart-m3.md), [quickstart-m4](doc/WEB/quickstart-m4.md), [m3-security](doc/m3-security.md), specs 025/026 and current transport/guard code |
| Formatting/styles | [formatting](doc/formatting.md), [031](specs/031-redgate-style-import/), [033](specs/033-format-styles-window/), [039](specs/039-sqlprompt-style-editor/spec.md), 040 style follow-ups |
| Completion | [campaign](doc/web-autocomplete-campaign-2026-07-16.md), [032](specs/032-autocomplete-remediation/), [corpus README](tests/completion-corpus/README.md) |
| AI | [028](specs/028-m6-ai-browser-closure/), [036](specs/036-kimi-chat-updater-fixes/), [037](specs/037-multi-ai-agents/), [browser privacy](doc/WEB/ai-privacy-commitment.md), [local CORS](doc/WEB/ai-local-provider-cors.md) |
| Public site | [034](specs/034-blazor-product-site/), [038](specs/038-site-downloads-admin-portal/); Site csproj and appsettings define docs ingestion/exclusions |
| User-facing wording/help | [topics/options](doc/topics/options.md), [topics/sql-history](doc/topics/sql-history.md), [topics/formatting](doc/topics/formatting.md), other [topics](doc/topics/) |
| Build/manual verification | [deployment](doc/deployment.md), [manual-test-plan](doc/manual-test-plan.md) (older), [UiTests README](tests/AkmlSql.UiTests/README.md), current feature quickstarts |

For the next request: inspect Git status and the relevant current files, use this
map to choose the correct layer, preserve existing behavior outside the requested
change, and verify the affected path with its established tests and UI checks.

## 11. Follow-up: product site screenshots (2026-10-06)

- Home and Features now use selected SSMS crops from the owner's
  `C:\Users\Administrator\Documents\AKML SQL screenshots` folder, copied unchanged to
  `src/AkmlSql.Site/wwwroot/img/screenshots/{dark,light}/`. Selected names: 01 SQL
  History, 02 Format styles, 03 wildcard picker, 05 JOIN suggestions, 10 command
  palette, 11 Options, 12 execution warning. Only `*-crop.png` files are used.
- [ProductScreenshot.razor](src/AkmlSql.Site/Components/ProductScreenshot.razor)
  centralizes the paired images, native dimensions, captions, alt text and full-size
  links. Home uses Format styles; Features has seven alternating media rows.
  `site.css` follows `html[data-akml-theme]`: light shows light, dark/high contrast
  show dark. High contrast has no supplied capture. Mobile keeps the whole crop.
- Browser verification exposed an existing enhanced-navigation issue: SSR patches
  reset the html theme attribute, stylesheet and picker visibility to defaults.
  `theme-toggle.js` now retains the active theme in memory and restores it on
  Blazor's `enhancedload`, including when storage is unavailable. Navigation does
  not turn an OS-derived default into a saved preference. Its deployed E2E test
  now covers navigation and matching screenshot visibility as well as reload.
- Verified: all 877 Site unit/component tests passed; full solution Release build
  with VS MSBuild passed (existing warnings); theme generation drift check passed.
  Local Chromium checked Home/Features at 1440, 390 and 320 px, all three themes,
  reload/navigation, full-size links, complete image ratios, OS defaults, unavailable
  storage and no-JavaScript fallback. All 14 copied images match source hashes.
  The local preview used a separate analytics database. Browser artifacts and its
  temporary verification script are in ignored `artifacts/site-screenshot-review/`.
  This work updated source only: no deployment was performed, and the
  deployed-site E2E suite was not run against the live installation.

## 2026-10-06 — Site admin and security audit checkpoint

- Reports: [admin functional/data review](reports/site-audit-2026-10-06/01-admin-functional-review.md)
  and [production security audit](reports/site-audit-2026-10-06/02-production-security-audit.md).
  Keep these in `reports/`, outside the public `doc/` ingestion tree.
- Reviewed deployed IIS settings and read-only production aggregates; the deployed
  Site assembly matched the Release build. Authenticated/mutating checks used an
  isolated copy of the published application, synthetic data and loopback SMTP.
  All 877 existing Site tests passed. This does not certify authenticated
  production behavior or the later download acceptance suite.
- Do not trust the comments claiming empty forwarded-header proxy lists ignore
  headers: both lists are cleared, which trusts arbitrary senders. The local
  published app reproduced IP spoofing and throttle bypass; production uses the
  same empty configuration. The report also records legacy TLS acceptance,
  analytics file ACL exposure, CSV formula handling, intake and privacy gaps.
- The owner explicitly requested a stop after Task 2. No application/security
  fix or deployment was made. Task 3's GitHub download investigation/fix and
  browser acceptance tests remain pending approval.

## 2026-10-06 — Admin fixes and collection health follow-up

- The owner subsequently approved F01–F10 and collection health first, including
  retention/error handling overlaps with the security audit. See
  [implementation and verification](reports/site-audit-2026-10-06/03-admin-implementation.md)
  and [spec follow-up](specs/038-site-downloads-admin-portal/admin-review-follow-up.md).
- People now uses latest known dimensions and matching filters/counts; full CSV
  exports have no 500-person cap. Feedback/errors have paging/search/exports;
  Insights has aggregate CSV. Numeric settings reject invalid input.
- Analytics reports use independent read-only SQLite snapshots. Settings shows
  process-lifetime queue delivery/loss/lag and maintenance status/current backlog.
  Maintenance runs at startup, hourly and after retention changes, retrying failures.
  Non-GET/API error responses no longer re-enter Razor status/form handling.
- Verified 890 Site tests, full solution Release build, theme drift gate and 12
  isolated local HTTPS Chromium check groups (including desktop/mobile widths).
  No deployment or Git mutation performed by this agent. Queue delivery remains
  best-effort; counters reset on restart. Broader security remediation, new desktop
  telemetry and Task 3 download work remain pending. Audit reports are historical
  baselines, not current source status; consult the implementation report too.
