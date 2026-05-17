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

await builder.Build().RunAsync();
