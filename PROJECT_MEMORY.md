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
4. Installer smoke tests run the real installer. Since the 2026-10-26 spec 045 safety
   gate they require `AKML_INSTALLER_SMOKE=1` as well as admin/IIS eligibility. Set
   that opt-in only on disposable VMs; routine runs exclude `Category=InstallerSmoke`.
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

## 2026-10-07 — Download click and latest-release follow-up

- The owner authorized fixing normal clicks that worked only in a new window.
  Reproduced: a page-local script did not initialize after Blazor enhanced navigation,
  leaving `/dl/` links intercepted as fetches with no native download. Installer links
  now render direct CDN URLs with enhanced navigation disabled; the global delegated
  handler queues one nonblocking count request and suppresses repeat clicks for two seconds.
- `LatestGitHubRelease` refreshes public GitHub latest metadata every 15 minutes,
  validates the installer asset, and persists `latest-github-release.json` beside the
  analytics DB. Last good metadata survives API failures and restart; the manifest
  seeds a cold start. Public download and admin settings preview use the same combined
  list. Version/size appear on the button; count attribution accepts API-only versions.
- Verified 900 Site tests, full solution Release build, theme drift, 33 online browser
  checks (Chrome/Edge/Playwright Firefox/mobile Chrome emulation) and 8 API-down restart
  checks. Actual latest EXE size/hash matched GitHub metadata. See
  [download implementation report](reports/site-audit-2026-10-06/04-download-fix.md).
  No deployment or Git mutation. Tracking remains best-effort; direct no-JS/context-menu
  downloads have no beacon. Broader security remediation and desktop telemetry remain pending.

## 2026-10-10 — Spec 041 web workspace continuation

- Current feature: `specs/041-web-ssms-ux-parity/`. US1–US5 were already complete when this
  continuation resumed T186 and T206–T216. Preserve that staged work; consult the current
  task ledger and baseline for the final verification state.
- The earlier web map is superseded in these places: results now use
  `Shared/ResultsPaneComponent.razor` and `Shared/ResultSetGrid.razor`, with helpers in
  `Shared/Grid/`; Settings is one routed page with sections in `Shared/Settings/` and
  contracts/catalog in `Services/Settings/`. IndexedDB is v4 (`workspaceLayout`).
- The editor has named documents, persisted panel layout, page-scoped keys, server Parse,
  GO batches, structured Messages and a session-preserving database selector. Engine host
  identity determines whether styles say “Shared with SSMS” or “On this engine”.
- Shared web helpers: `akml-ui.js` (dialogs/focus/clipboard/keyboard help),
  `akml-results-grid.js` (grid pointer/keyboard/measurement), and `akml-workspace.js`
  (page keys/splitters). Lint gutter markers now expose their existing tooltip on focus.
- Verification on 2026-10-10: full Engine 2,091 passed; Formatting 1,635; Shell 882;
  completion corpus unchanged at 1,311/1,343. Full Web E2E: 78 passed, 10 existing skips;
  final color/screenshot follow-up: 10/10 passed. Web: 1,131 passed with 42 known formatter
  failures; Core: 1,285 passed with one known failure and three skips. Site: 904;
  IntelliSense: 25; Node: 97. Release solution build and both theme drift checks passed;
  SC-008 found no regressions or missing prior passes. Build Debug web and engine before E2E, finish builds
  before starting sandbox engines, and run browser classes serially. Exclude both screenshot
  tours from routine suite comparisons.
- The local workspace screenshot tour captures three themes at 1440×900 and 1100×700.
  All 90 images were refreshed; nine of the twelve tasks open at resumption are complete.
  T209, T215 and T216 remain unchecked for the acceptance prerequisites below.
  Native Windows keyboard/layout checks, the full manual quickstart, the hallway study and
  post-deployment product-site screenshots are separate acceptance items. No installed application
  files were changed. The existing full E2E suite also exercises the installed bridge's service
  restart/reconnect path and restores it to Running; spec 041 browser tests use sandbox engines.
- Final audit fixes: web-only status-text/badge tokens and inverse high-contrast system colors;
  auto-fit includes the cell border; `GridRowBoundary` skips unchanged rows on pointer updates;
  a Live bridge hides stale startup pairing failures. `QuickstartAcceptanceTests` checks schema
  controls, width persistence and loss of the sandbox engine during execution.

## 2026-10-11 — Spec 041 approved deployment follow-up

- User-authorized earlier changes were committed separately as 01150c0, debbbbc,
  7f21566, f5a7def and 15c3612. Later changes remain uncommitted.
- Web and Engine 1.26.1010.2224 were deployed with explicit approval to IIS AkmlSqlWeb
  and service AkmlSqlWebEngine. The actual IIS URL is http://localhost:59416/;
  bridge port remains 59417. Both previous payloads and root ACLs are backed up under
  %LOCALAPPDATA%/AKML SQL/spec041-deployment/backup-20261011-102256.
  Existing IIS web.config, ProgramData configuration, certificates and user data were
  preserved. The first port-80 smoke check rolled back; the corrected deployment passed.
- SiteScreenshotTour accepts AKML_DEPLOYED_WEB_URL. It passed 2/2 against the installed
  release; Northwind returned 50 rows with matching version labels. The two web product
  images were inspected and refreshed in the repository. The marketing site was not deployed.
- Native Windows input found that Edge reserves Ctrl+Shift+S. Save as now also accepts
  Ctrl+K, Ctrl+A, advertised in the toolbar, View menu and palette. The opt-in
  NativeKeyAcceptanceTests uses guarded Windows SendInput; exclude Category=NativeAcceptance
  from unattended suites. Final native replay was blocked by foreground focus/setup issues.
- Expanded quickstart/tab acceptance passed 10/10; DOM browser key checks passed 3/3,
  collision checks 6/6, Node 98/98. Web retains 1,131 passes and 42 known failures.
  SC-008 still reports zero regressions/missing prior passes. Release Web publish passed.
- T216 is now complete: ten of the original twelve unfinished tasks are complete.
  T209 needs a clean native-input replay. T215 still needs actual SSMS-started style
  grouping checks and human spreadsheet paste; the hallway study awaits
  ten participants. See specs/041-web-ssms-ux-parity/baseline.md for precise evidence.

Follow-up: the final four focused browser cases passed (three-browser Save as menu/palette
checks and real-engine duplicate-style source labels). The native retry skipped all three
cases because Windows foreground focus was unavailable; T209 remains open.

### Native keyboard acceptance completed — 2026-10-11

After the user confirmed the interactive desktop was unlocked, NativeKeyAcceptanceTests
passed 3/3 in Chromium, Edge and Firefox, with no failures or skips. Real Windows
scan-code input verified all claimed keys, unchanged input language for grid Shift+Alt
arrows with English/Arabic installed, and Polish Programmers AltGr inserting óń.
Edge reserves Ctrl+Shift+S for Screenshot; Ctrl+K, Ctrl+A opened Save as successfully,
with the alternative visible in the View menu and palette. Original browser layouts
were restored. T209 is complete; eleven of the original twelve remaining tasks are done.
Only T215 remains for actual SSMS-started-engine style grouping and human spreadsheet
paste; the ten-user hallway study awaits participants. This supersedes the earlier
native-input foreground-focus blocker. No product code or deployment changed in this replay.

### Actual SSMS/Northwind acceptance — 2026-10-11

Opened a dedicated SSMS 22 instance with isolated AKML app data and a saved read-only
Northwind query. SSMS executed six Customers rows. Playwright paired with its actual
child engine and verified a four-row/three-column copy (ANTON through BLAUS).
The installed extension had a private Engine 1.26.1006.2219, distinct from the web
service payload; it lacked the expected Shared with SSMS label. With additional explicit
user approval, backed up that private Engine folder and root ACL to
%LOCALAPPDATA%/AKML SQL/spec041-deployment/ssms-engine-backup-20261011-203436,
deployed the verified 1.26.1010.2224 payload (319 hashes match), and reopened SSMS.
Playwright then passed both Shared with SSMS screens, duplicate style source labels,
Northwind execution and clipboard checks with zero JavaScript errors. Extension DLLs
and user data were preserved. SSMS remains open on the saved Northwind script.
T215 now only lacks actual spreadsheet paste (no Excel/LibreOffice found) and the
participant-dependent hallway study. Evidence: %TEMP%/spec041-ssms-acceptance/.

### Spreadsheet acceptance completed — 2026-10-12

T215 is complete; all twelve tasks originally open in spec 041 are now checked.
Playwright copied live Northwind results to the Windows clipboard; LibreOffice Calc
Portable 26.2.4 pasted them into real cells. Verified four rows by six columns exactly,
including accented names/cities, literal NULL, ISO date text and all 2,000 characters
of each long value. A second paste retained the CompanyName header and six complete
names. Calc requires Tab-only import for a single text column; automatic detection can
split at spaces. The workbook, screenshot and JSON verification are recorded under
specs/041-web-ssms-ux-parity/baseline/acceptance/. The portable package checksum and
signature were verified; its files live under %LOCALAPPDATA%/AKML-Acceptance-Tools.
SC-006 still awaits ten real participants, explicitly permitted as pending by T215.
This was automated desktop acceptance, not a claimed human usability study. No product
source changes or new Git mutations were made for this check.

### PR #255 review verification follow-up — 2026-10-13

Verified review commit 759c6e5 with fresh Debug Web/Engine builds, live SQL and the
full Playwright behavioral cycle. Browser checks found and fixed JSON control-character
escaping/nonfinite fallback and a late double-click changing the active grid editor;
those corrections and ten review regression cases are now in the branch. The full
Engine suite passed 2,118 cases. Core's one positional-contract failure and Web's 42
formatter failures match the prior baseline; no goldens were changed. Detailed suite
counts and evidence are in specs/041-web-ssms-ux-parity/baseline.md.

The stronger touch test exposed an input-generation issue reproduced outside AKML:
a burst of CDP drag moves can suppress the next tap. Pacing the drag steps restores
normal taps. Both splitter and column resizing, followed by actual Schema restoration,
pass at 100% and 150%. Removed temporary event tracing. Current native keyboard checks
skipped because Windows refused foreground focus; the earlier 2026-10-11 native pass
is historical evidence only. Installed payloads were not updated during this review.

Final updated-branch checks: Playwright review/theme/touch follow-up 14/14; deduplicated
browser outcomes 98 passed, 0 failed, 10 existing skips; shell suite 927/927; schema
components 16/16; updater unit checks 43/43. Full Release solution build and both theme
drift checks pass. Raw evidence is under %LOCALAPPDATA%/AKML SQL/spec041-review-759c6e5/.
The three native cases remain skipped due to unavailable foreground focus. This
follow-up changes the touch test and verification records only; no Git mutations or
new deployment were performed by this agent.

### Local Playwright runner — 2026-10-13

Added scripts/run-playwright.ps1. Running it from any working directory builds Debug
Engine, Web and E2E tests, installs the bundled Playwright Chromium/Firefox versions,
runs the behavioral suite serially, and opens an HTML report. TRX/HTML results use a
new timestamped directory under %LOCALAPPDATA%/AKML SQL/playwright-results. Failure
exit codes and empty test selections are reported; existing reports are not overwritten.
-Headed uses the shared BrowserLaunch helper for regular and persistent browser launches.
-IncludeNative enables the desktop keyboard checks; -IncludeScreenshots additionally
runs the image generators. SQL/service/desktop prerequisites and focused-run options
are documented in the script's Get-Help text. No installed application is deployed.

### Playwright report failure corrected — 2026-10-13

Inspected playwright-results/20261013-105553-385/playwright.html and its TRX:
97 passed, 1 failed, 13 skipped. Firefox saw two Save as palette rows. The editor's
asynchronous initialization could resume after navigation disposed the page, then
register commands whose disposal token was never released. A deterministic lifetime
regression reproduced the leaked commands before the fix. Initialization now stops
after awaited loads when the editor is disposed, before attaching more handlers or
registering commands. The browser's unique-command assertion remains unchanged.

The 13 focused lifetime/command-registry unit tests pass. The headed Playwright rerun
passes all three browser keyboard cases (Chromium, Edge, Firefox) plus persistent
browser reopening: 4 passed, 0 failed, 1 expected spike-page skip. The runner now counts
NotExecuted result entries, since this adapter reported Counters.notExecuted=0 despite
13 skips in the original report. Its console correctly reports the single follow-up skip.
The original report is preserved; new HTML/TRX evidence is under
%LOCALAPPDATA%/AKML SQL/report-fix-20261013/. Original skips are opt-in native checks,
missing alias/spike configuration, and pre-existing scenario placeholders. This was
a focused fix verification, not another full-suite run.

## 2026-10-17 — Web review fixes (AKML_WEB_REVIEW_2026-10-17.md)

The implementation pass reported all 22 findings fixed; those changes are now committed as
`1d09ef4c`. Its per-ID ledger remains at the end of the original review file (root, not doc/).
**That completion claim is superseded by the independent follow-up below: seven remain partial.**
Facts that changed:

- Editable results are bound to the connection they were read through: `ExecuteQueryResult.ConnectionStamp`
  (key 15, HMAC under a per-process key) goes back as `ApplyChangesRequest.ConnectionStamp` (key 6) and the
  engine refuses a stale one; the web freezes results read under an older `ISqlConnectionService.ConnectionGeneration`.
  Grids are also frozen while a query runs or their Apply is waiting.
- Disconnect is a real engine operation: `SessionDisconnect` 225 → 226 (`session.disconnect.v1`). Every
  session-connection close rolls back an open transaction explicitly.
- SQL Server Connect tests the login first (as PostgreSQL already did). Remote SQL Server connections are
  encrypted and certificate-validated unless the saved connection's "Trust the server certificate" is ticked;
  loopback still skips validation.
- LAN installs serve the site over HTTPS on a "Secure site port" (default 443, `/WEB_HTTPS_PORT`, registry
  `HttpsPort`) with the bridge certificate; the app refuses to ask for secrets in an insecure context.
- `akml-crypto.js` creates the wrapping key once (one readwrite transaction, shared promise) and closes its
  IndexedDB connections. AI schema context resolves the connected database only. AI streams that end badly
  raise `AiStreamException`; provider error bodies never reach messages or diagnostics, and the diagnostics
  ring masks credential-shaped text (`DiagnosticRedaction`).
- `ColumnProvenanceDto.HasDefault` (key 12): new grid rows send only the cells the user set; an empty row is
  `INSERT … DEFAULT VALUES`.
- Browser regressions: `tests/AkmlSql.Web.E2E.Tests/SecretStorageTests.cs` (no engine needed) and
  `ReviewFixBrowserTests.cs`. Not verified: a real LAN install over HTTPS, Firefox, a PostgreSQL server,
  a screen reader.

### Independent follow-up review — 2026-10-17

See [AKML_WEB_RE_REVIEW_2026-10-17.md](AKML_WEB_RE_REVIEW_2026-10-17.md) for the current disposition:
15 original findings resolved for the current build, 7 partial (WEB-03, 07, 11, 13, 18, 20, 21),
plus an additional History full-SQL retrieval failure path. Eight actionable application findings
cover cross-engine schema-cache collisions, executable History excerpts, HTTPS binding ownership,
snippet save races, restored diagnostic secrets, SQL Server inserted-row values, persisted AI
incomplete status, and keyboard scrolling. Dependency advisory remediation is tracked separately.

Fresh verification: Web 1,632/1,632; JavaScript 131/131; serial Chromium 48/48; targeted Engine 97/97
(live local SQL included), with no skips in those .NET runs. Six separate independent acceptance
probes fail and reproduce remaining defects; an additional browser probe reproduces invisible
Snippets keyboard focus. Evidence and replayable probe sources: `.review-artifacts/2026-10-17-followup/`.
Temporary probe tests were removed from the normal test project after recording evidence, and that
project was rebuilt. This was review/documentation work, with no production fixes or deployment.
Actual LAN installer operation, remote certificate validation and live PostgreSQL remain unverified.

## 2026-10-17 — Web re-review fixes (AKML_WEB_RE_REVIEW_2026-10-17.md)

The follow-up review's REV-01..REV-08 and SEC-01 are fixed in the working tree (uncommitted); per-ID matrix at the end of that report.
Facts that changed:

- `ISqlConnectionService.SchemaIdentity` (the browser schema-cache key) is now `"<server> (engine <host:port>)"`, scoped to the engine the
  session was opened through (`SqlConnectionService.ScopeToEngine`): a loopback SQL address is relative to the engine. Older unscoped
  cache entries no longer match and are refetched. The AI schema provider also re-checks the connection generation.
- History never copies, opens or re-executes the 500-character search excerpt: those wait for the full SQL (or a stored version).
- LAN HTTPS binding ownership: `src/AkmlSql.Installer/web-https-binding.ps1` (decision functions + `-RemoveOwned`), record at
  `%ProgramData%\AKML SQL Web\https-binding.json`, markers `https-site.ok` / `https-site.failed`; uninstall removes only AKML's still-owned
  binding. Script-level tests: `tests/AkmlSql.Installer.Tests/HttpsBindingDecisionTests.cs` (no admin needed).
- Diagnostics ring is persisted as a versioned envelope; a legacy (pre-fix) ring is migrated on restore (AI entries purged).
- `ChatTurn.IncompleteReason` persists an incomplete AI answer's status.
- SQL Server inserts return the stored row via `OUTPUT INSERTED` when the table has no enabled trigger; `ExecuteResultSet.DefaultsKnown`
  (key 13).
- Dependencies: MessagePack 3.1.9 (Core, Engine, E2E tests); System.Security.Cryptography.Pkcs pinned to 9.0.21 in the Engine.
- Running `ISCC /O-` deletes `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe`; for a syntax check compile with `/O<scratch dir>` instead.

## 2026-10-17 — Third independent Web review

The latest verdict is [AKML_WEB_REVIEW_ROUND3_2026-10-17.md](AKML_WEB_REVIEW_ROUND3_2026-10-17.md),
which supersedes the preceding blanket claim that every follow-up finding is fixed. The fixes were
committed as `1e08a9c1`; the checkout advanced to `3826c552` during review. REV-06 remains partial:
triggered SQL Server inserts return only an identity, leaving other committed values absent from
the grid. New REV-09: trigger detection queries the session catalog even when the insert targets
another catalog, causing a cross-database triggered insert to fail with OUTPUT-without-INTO.
Both were independently reproduced against uniquely named disposable tempdb tables through the
real Engine router. The two desired-behavior tests are `Round3InsertAuditTests` (a concurrent commit
captured them in the Engine test project); they currently fail and must not be weakened to close
the review. Evidence copies and results are in `.review-artifacts/2026-10-17-round3/` (now ignored).

The other follow-up fixes pass their relevant component/service/JS tests; HTTPS ownership has
14 passing decision tests but still lacks disposable-host install/repair/uninstall and second-PC
acceptance. Web/Engine dependency audits report no vulnerable packages from current sources;
Engine resolves MessagePack 3.1.9 and Pkcs 9.0.21. Final verification: Web 1,656 passed, JavaScript
133 passed, selected serial Chromium 17 passed, installer ownership decisions 14 passed; targeted
Engine 93 passed plus the 2 failing independent acceptance probes, no skips in those .NET runs.
See the new report for build provenance and scope.


## 2026-10-18 — Round-3 fixes (REV-09, REV-06 remainder)

- SQL Server grid inserts read the stored row back by key: `InsertReadBack` (Engine/Execution) loads the target table's enabled
  triggers + key (PK → narrowest unfiltered unique index → identity) from `[catalog].sys.*` of the TABLE's database, then each insert
  runs `OUTPUT INSERTED.<key> INTO @akml_k` + `SET @akml_id = SCOPE_IDENTITY()` + `SELECT ReturnColumns ... WHERE key IN @akml_k`.
  Table-variable key columns carry the column's collation (a cross-database BIN2 key fails without it).
- `CrudEditResult.Row == null` for a successful SQL Server insert means "not read back"; the grid (`_unconfirmed` in
  `ResultSetGrid.razor`) shows unknown cells as "…", refuses edit/delete of that row until the query re-runs.
- E2E: Apply's test id is `results-apply` for a single result set and `results-apply-n` only when sets are stacked — match
  `button[data-testid^='results-apply']`. `WithSqlOrSkipAsync(database: "master")` fails to connect here; use `USE master;` in the batch.
- Full Engine runs alongside the browser suite can trip `RefactoringPerformanceTests` budgets; re-run alone.

### Independent verification and additional corrections — 2026-10-18

See [AKML_WEB_FIX_VERIFICATION_2026-10-18.md](AKML_WEB_FIX_VERIFICATION_2026-10-18.md).
The original two round-3 acceptance tests pass with the staged fixes. Additional live SQL testing found that
an INSTEAD OF INSERT trigger could make the attempted key match an unrelated existing row. `InsertReadBack`
now checks the target catalog's `sys.trigger_events` and disables read-back for every enabled INSTEAD OF INSERT
trigger. The insert may still complete, but the grid cannot treat any row under that attempted key as confirmed.
Unconfirmed grid-row metadata now follows the `ExecuteResultSet` object's lifetime through a `ConditionalWeakTable`,
surviving component recreation without adding wire fields; a newly fetched result clears the state. User guidance
asks for a SELECT to check stored values. Both new regressions failed before correction and pass afterward.
Copy/export also refuses unconfirmed rows rather than turning their unknown values into SQL NULL; copying confirmed
rows remains available. Three additional Copy/CSV/TSV regressions reproduced the prior false-success behavior.
The latest report records this run's results separately from earlier agents' results. HTTPS deployment acceptance
still needs a disposable IIS host and a second machine.
Final checks for this follow-up: Engine targeted 112 passed; Web full 1,662 passed; Chromium 30 passed plus a
focused final clipboard regression. A snippet test assertion was made to await its async click handler/save
readiness after a reproduced timing failure; production snippet behavior was unchanged. Evidence and scope
are in `.review-artifacts/2026-10-18-fix-verification/` and the root verification report.

## 2026-10-22 — Product Studio public website (spec 044)

The owner selected concept A from `reports/marketing-homepage-2026-10-22`. The Site
homepage now explains benefits, distinguishes SQL Server (SSMS 22/browser) from
PostgreSQL (browser/enabled engine), and includes a five-item screenshot tour, optional
AI guidance, setup choices and FAQ. Features/Download/footer copy is aligned; public
browser setup is `doc/topics/browser-setup.md` (`/docs/topics/browser-setup`).

Implementation: `Components/Pages/Home.razor`, `wwwroot/css/product-studio.css`,
`wwwroot/js/product-tour.js`. The tour is progressive enhancement: all panels remain
available without JS. `ProductScreenshot` has optional DarkPath/LightPath parameters
for actual browser captures while keeping the existing Name convention. The existing
screenshot viewer handles zoom/gallery/focus and accepts media h2 or h3 headings.
New Marketing tokens in `docs/theme-tokens.json` are generated into both Site/Web CSS;
existing editor/desktop values and theme preference behavior remain unchanged.

Verification: full Release MSBuild restore/build and both theme drift checks passed;
Site tests 909 passed with one existing duplicate Options-help-anchor failure; 15
Chromium theme/viewport combinations and the tour/no-JS/navigation checks passed.
See `specs/044-site-product-studio/baseline.md` for exact limits and screenshots.

## 2026-10-26 — Standalone web hosting, partial local implementation (spec 045)

`BridgeOptions.WebRoot` is optional and empty by default. `EngineHost.RunWebAsync`
alone maps it to the existing `WebSocketTransport`; desktop `RunAsync` never serves
the bundle. `StaticWebResponder` validates the root against private config/token
folders, builds a public extension manifest excluding hidden/system/reparse entries
and known state names, serves GET/HEAD with validators and the IIS CSP, and bounds
file streams to 32 slots with deadlines. `/akmlsql` keeps the plain bridge page;
an absent/refused root keeps plain-page behavior everywhere. No IPC change.

Web `PageEngine` detects `X-AKML-Host: engine` using same-origin `HEAD /`. With no
active saved connection, auto-connect derives the page host/port/scheme, reuses a
matching record or saves it after an OK handshake. HTTPS page records persist
`StrictScheme=true` and try only `wss`; port 443 is permitted for those records.
Saved active choices still win. PIN-required answers prefill Settings › Connections.
The local-engine action also reuses a saved bearer for a matching page engine.

Installer scripts now expose pure config/firewall/readiness/answer-check helpers;
the thin installer readers compile. The existing IIS/default wizard remains until
its migration and UI spikes are verified. Real installer smoke tests require
`AKML_INSTALLER_SMOKE=1` plus admin/IIS eligibility and must run on disposable VMs.
The owner reported no VMs and authorized independent local work. S1–S4/S6/S7,
installer integration and installed-machine acceptance remain pending. No owner
installation or deployment has been changed. See `specs/045-web-standalone-hosting/`
tasks and baseline for current counts, browser evidence and known baseline failures.

The new engine-served browser group has 7 passed and 1 failed: direct loads of all
ten routes reach Live and `/spike` formats/runs its SQL corpus, but the strict
network assertion sees Chromium `net::ERR_ABORTED` on WASM `HEAD /` detection.
Native JavaScript HEAD succeeds. T034 remains unchecked and SC-006 compliance is
not claimed. Attempts at response buffering/lifetime changes did not solve it and
were removed; the strict test remains enabled for the next implementation pass.

### Owner-authorized continuation — remaining installer source

The owner then explicitly deferred T034 to Claude Code and asked to implement the
rest. `web-installer.iss` now owns Service/IIS/None choices, ports/exposure,
pre-install protection and post-install configuration/check/summary. Setup keeps
hidden legacy component names through `WebLegacyComponent`; a literal `Check:
False` fallback is rejected by the local compiler. Readiness and custom ownership
use provisional conservative choices pending the unrun VM spikes.

Permanent Support helpers now include `web-iis-remove.ps1` (owned site/pool/binding
cleanup with conservative unknown-state handling) and `web-hosting-repair.ps1`
(location/ACL checks before and after elevation, either host). State DACLs are set
exactly, including removal of old explicit grants. Custom install trees reject
foreign owners and reparse points. Repair uses Windows PowerShell 5.1 filesystem
ACL APIs directly, avoiding a security-module autoload dependency observed in
the isolated test environment.

`PrepareToInstall` hardens before stopping applications and retires old uninstall
logs containing UTF-16 temporary script paths. The detector builds markers at
runtime so it does not match its own compiled constants. Failure restores old
uninstall files, preserving partial files and logging I/O failures. Silent uninstall
keeps data/settings without prompts. Real service/IIS/LAN/silent-uninstall smoke
cases are implemented but stay behind `AKML_INSTALLER_SMOKE=1` on disposable VMs;
none was run on the owner's computer. Engine/Web were freshly published for a
review installer under `.review-artifacts/045-final-iscc`, never installed.

Current docs describe the new source behavior. `specs/045-web-standalone-hosting/`
baseline and `CLAUDE_HANDOFF.md` distinguish passing local checks from T034,
existing Core/performance/Site failures, unrun VM rows and owner upgrades. No Git
mutation was performed; preserve the existing staged and unstaged work.
This is source work, not a deployment or PostgreSQL execution verification. Site E2E's
default fixture targets deployed IIS; use an isolated published preview for source QA.

### Server continuation and runtime verification — 2026-10-27

The owner resumed missing spec 045 tasks and requested SSMS, browser and Windows-service
testing. T034's strict browser failure is resolved: PageEngine now calls the small native
`js/akml-page-engine.js` HEAD probe through JS interop. It keeps the existing root/header,
no-cache, five-second timeout, three-outcome cache and engine precedence. WASM HttpClient
response disposal produced aborted HEAD requests; the original strict ten-route/network
test passes with native fetch. No static-responder workaround or relaxed assertion was kept.

`StandaloneQueryCycleTests` covers engine-served auto-connect, SQL Server temp-table state,
persisted text/connection reload and another query. Its explicit
`AKML_STANDALONE_VERIFY_URL` branch targets an isolated loopback service. A fresh published
engine ran as a temporary LocalSystem SCM service: query/reload and real crash recovery
both passed, and the test service was deleted. Existing installed-service reconnect tests
also stop/start AkmlSqlWebEngine; it was left Running, with its binary and hosting config
unchanged. This is runtime verification, not an installer upgrade or LAN/second-PC acceptance.

SSMS 22 is installed here. After the owner reconnected RDP, the installed extension
(`1.26.1022.701`, active Khamis Style) formatted and executed the read-only tempdb query
`SELECT 42 AS AKML_CYCLE_RESULT_045`. The result grid and success status were inspected.
`InstalledSsmsCycleTests` records the pipe, UIA and screenshot evidence. This host's UIA
editor text includes line numbers; matching its success status by a Name condition avoids
unrelated accessibility elements. The shell source and installed payload were not changed.

The old Core/Site red assertions were stale: ProfileGet now tests all nine existing keys
and a legacy seven-key payload; Options help tests pin the exact General/History combined
page aliases while retaining uniqueness for every other topic. Both full suites pass.
The browser formatting test now waits for EditorComponent's applied `data-tab-id` before
typing; that marker is set after JS applies the state, with an interop-ordering unit test.
Remaining VM spikes, real installer matrices and owner upgrades remain unchecked. Use the
latest section of spec 045's baseline.md for final counts and any release-gate limitation.
