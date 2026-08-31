using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using AkmlSql.Web;
using AkmlSql.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Spec 021 DI registrations.
//
// Foundation:
//   IIndexedDbAdapter -> JsIndexedDbAdapter (real IndexedDB via akml-indexeddb.js).
//     Tests bind InMemoryIndexedDbAdapter via constructor injection.
//   IThemeApplier -> JsThemeApplier (calls into akml-theme.js).
//
// M2 services:
//   IFormatterService          T037 -- wraps FormatterPipeline in-process.
//   IAnalyserService           T043 -- wraps AnalysisEngine from AkmlSql.Analysis.
//   IThemeService              T034 -- OS-preference + IndexedDB-backed user override.
//   IProfileStore              T038 -- built-in + user profiles persisted to IndexedDB.
//   IAnalysisSettingsStore     T044 -- analyser settings persistence.
//   IEditorSessionStore        T051 -- editor session persistence + restore.
//   IDiagnosticsRingBuffer     T048 -- fixed-size ring with debounced IndexedDB flush.
builder.Services.AddSingleton<IIndexedDbAdapter, JsIndexedDbAdapter>();
builder.Services.AddSingleton<IThemeApplier, JsThemeApplier>();

builder.Services.AddSingleton<IFormatterService, FormatterService>();
builder.Services.AddSingleton<IProfileStore, ProfileStore>();
builder.Services.AddSingleton<IAnalysisSettingsStore, AnalysisSettingsStore>();
// Spec 027 T024/T026 (US4): AnalyserService honours the browser-local per-rule overrides
// from IAnalysisSettingsStore. Registered via an explicit factory so the store is
// unambiguously injected (rather than relying on DI to fill the optional ctor param) —
// "Suppress globally" depends on this same store instance being read on the next analyse.
builder.Services.AddSingleton<IAnalyserService>(sp =>
    new AnalyserService(sp.GetRequiredService<IAnalysisSettingsStore>()));
builder.Services.AddSingleton<IThemeService, ThemeService>();
builder.Services.AddSingleton<IEditorSessionStore, EditorSessionStore>();
builder.Services.AddSingleton<IDiagnosticsRingBuffer, DiagnosticsRingBuffer>();

// Spec 030: ⌘P command palette registry. Singleton so the palette (hosted in MainLayout) and
// context-bearing pages (Editor.razor) share one registry — pages register actions on mount and
// dispose the returned token on unmount.
builder.Services.AddSingleton<ICommandRegistry, CommandRegistry>();

// Spec 021 M3 (browser-side bridge):
//   IWebCryptoWrapper      T069 -- AES-GCM wrap via wwwroot/js/akml-crypto.js.
//   IPairingTokenVault     T069 -- bearer tokens at rest, bound to connectionId via aad.
//   IConnectionStore       T067 -- EngineConnection records persisted to IndexedDB.
//   IEngineBridge          T068 -- WebSocket client + handshake + reconnect.
//
// The Func<IBridgeWebSocket> factory is registered separately so EngineBridge can
// spin up a fresh socket per ConnectAsync call (the socket itself isn't reusable
// once the underlying WebSocket closes).
builder.Services.AddSingleton<IWebCryptoWrapper, JsWebCryptoWrapper>();
builder.Services.AddSingleton<IPairingTokenVault, PairingTokenVault>();
builder.Services.AddSingleton<IConnectionStore, ConnectionStore>();
builder.Services.AddTransient<IBridgeWebSocket, JsBridgeWebSocket>();
builder.Services.AddSingleton<Func<IBridgeWebSocket>>(sp => () => sp.GetRequiredService<IBridgeWebSocket>());
builder.Services.AddSingleton<IEngineBridge, EngineBridge>();

// Keeps the bridge up without the user asking: startup connect, bounded-backoff retry, and a
// wake-up when the tab regains focus or the browser comes back online. Singleton because there is
// exactly one bridge and every page that loads calls StartAsync on it (idempotent by design).
builder.Services.AddSingleton<IEngineAutoConnect, EngineAutoConnect>();
// Spec 030: browser-side "Connect to SQL Server" — sends ConnectionChanged/DocumentChanged over the
// bridge using one canonical SessionId, enabling live-schema IntelliSense (Windows or SQL auth).
builder.Services.AddSingleton<ISqlConnectionService, SqlConnectionService>();

// Spec 030 — Phase 5: query execution + results grid + inline CRUD. QueryExecutionService routes
// ExecuteQuery / ExecuteCancel / ApplyChanges through the bridge using the canonical SessionId;
// ExecutionSettingsStore persists the advisory max-rows / timeout caps (the engine re-clamps).
builder.Services.AddSingleton<IExecutionSettingsStore, ExecutionSettingsStore>();
builder.Services.AddSingleton<IQueryExecutionService, QueryExecutionService>();
// Spec 030 — web SQL History: read/search/record/manage via the engine's history IPC (40/41/42)
// over the bridge (shared per-user store; no new engine code). Editor.razor records user-initiated
// executions; the /history page reads + manages them.
builder.Services.AddSingleton<IHistoryService, HistoryService>();

// Phase 4 (web connection manager): saved SQL-Server connections (IndexedDB, no password) + the
// modal-opener singleton that the command palette / Settings / StatusBar call to surface the modal.
builder.Services.AddSingleton<ISavedSqlConnectionStore, SavedSqlConnectionStore>();
builder.Services.AddSingleton<IConnectionManagerController, ConnectionManagerController>();

// M3.5 bridge-routed services (T072, T073, T074). Each routes through the bridge
// when open; otherwise returns an empty response. M5/T109 will install the
// IndexedDB-cache fallback for completion + signature + quick-info.
builder.Services.AddSingleton<ICompletionService, CompletionService>();
builder.Services.AddSingleton<ISignatureHelpService, SignatureHelpService>();
builder.Services.AddSingleton<IQuickInfoService, QuickInfoService>();
builder.Services.AddSingleton<IGotoDefinitionService, GotoDefinitionService>();
builder.Services.AddSingleton<IWildcardExpansionService, WildcardExpansionService>();

// M5 schema-cache + snippet + refactoring services.
//   ISchemaCacheStore     T107 -- IndexedDB persistence with composite (server, db) key.
//   ISchemaSync           T108 -- 30 s polling timer with 5 min idle suspend.
//   ISchemaCacheEvictor   T110 -- LRU eviction on QuotaExceededError.
//   ISnippetStore         T114 -- built-in + user snippets persisted to IndexedDB.
//   IRefactoringService   T117 -- lightweight local + heavyweight via bridge.
builder.Services.AddSingleton<ISchemaCacheStore, SchemaCacheStore>();
builder.Services.AddSingleton<ISchemaSync, SchemaSync>();
builder.Services.AddSingleton<ISchemaCacheEvictor, SchemaCacheEvictor>();
builder.Services.AddSingleton<ISnippetStore>(sp => new SnippetStore(
    sp.GetRequiredService<IIndexedDbAdapter>(),
    sp.GetRequiredService<IEngineBridge>()));
builder.Services.AddSingleton<IRefactoringService, RefactoringService>();

// M6 AI in the browser. Direct-to-provider with allow-listed origins;
// API keys wrapped at rest via Web Crypto.
//   IAiKeyVault       T125 -- wrap/unwrap user API keys, aad bound to providerId.
//   IAiPreference     T126 -- active providerId singleton.
//   IAiClientFactory  T128 -- per-provider HTTP client with origin allow-list.
//   IAiPromptService  T129 -- prompt builders from AkmlSql.AI + chat fetch.
// Spec 028 (M6 closure):
//   IAiFeatureSettings        T006 -- global + per-feature privacy disclosure modes + ghost-text knobs.
//   IAiSchemaContextProvider  T007 -- resolves prompt schema text from the M5 cache per mode.
builder.Services.AddSingleton<IAiKeyVault, AiKeyVault>();
builder.Services.AddSingleton<IAiPreference, AiPreference>();
builder.Services.AddSingleton<IAiFeatureSettings, AiFeatureSettingsStore>();
builder.Services.AddSingleton<IAiSchemaContextProvider, AiSchemaContextProvider>();
builder.Services.AddSingleton(sp => new System.Net.Http.HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});
builder.Services.AddSingleton<IAiClientFactory, AiClientFactory>();
builder.Services.AddSingleton<IAiPromptService, AiPromptService>();
// Spec 028 (M6) T034 (US6): local-only persisted chat conversations.
builder.Services.AddSingleton<IChatHistoryStore, ChatHistoryStore>();
// Spec 028 (M6) T031 (US5): direct-to-provider inline ghost-text completion.
builder.Services.AddSingleton<IAiGhostTextService, AiGhostTextService>();

await builder.Build().RunAsync();
