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
builder.Services.AddSingleton<IAnalyserService, AnalyserService>();
builder.Services.AddSingleton<IThemeService, ThemeService>();
builder.Services.AddSingleton<IProfileStore, ProfileStore>();
builder.Services.AddSingleton<IAnalysisSettingsStore, AnalysisSettingsStore>();
builder.Services.AddSingleton<IEditorSessionStore, EditorSessionStore>();
builder.Services.AddSingleton<IDiagnosticsRingBuffer, DiagnosticsRingBuffer>();

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

// M3.5 bridge-routed services (T072, T073, T074). Each routes through the bridge
// when open; otherwise returns an empty response. M5/T109 will install the
// IndexedDB-cache fallback for completion + signature + quick-info.
builder.Services.AddSingleton<ICompletionService, CompletionService>();
builder.Services.AddSingleton<ISignatureHelpService, SignatureHelpService>();
builder.Services.AddSingleton<IQuickInfoService, QuickInfoService>();
builder.Services.AddSingleton<IGotoDefinitionService, GotoDefinitionService>();

// M5 schema-cache + snippet + refactoring services.
//   ISchemaCacheStore     T107 -- IndexedDB persistence with composite (server, db) key.
//   ISchemaSync           T108 -- 30 s polling timer with 5 min idle suspend.
//   ISchemaCacheEvictor   T110 -- LRU eviction on QuotaExceededError.
//   ISnippetStore         T114 -- built-in + user snippets persisted to IndexedDB.
//   IRefactoringService   T117 -- lightweight local + heavyweight via bridge.
builder.Services.AddSingleton<ISchemaCacheStore, SchemaCacheStore>();
builder.Services.AddSingleton<ISchemaSync, SchemaSync>();
builder.Services.AddSingleton<ISchemaCacheEvictor, SchemaCacheEvictor>();
builder.Services.AddSingleton<ISnippetStore, SnippetStore>();
builder.Services.AddSingleton<IRefactoringService, RefactoringService>();

await builder.Build().RunAsync();
