using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using AkmlSql.Web;
using AkmlSql.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Spec 021 DI registrations.
//   T037 FormatterService       — real impl wraps FormatterPipeline (this session)
//   T034 ThemeService           — stub until OS-preference detection + IndexedDB land
//   T043 AnalyserService        — stub until the analyser library extraction (F1) lands
//   T048 DiagnosticsRingBuffer  — stub until IndexedDB-backed ring + export-bundle land
builder.Services.AddSingleton<IFormatterService, FormatterService>();
builder.Services.AddSingleton<IAnalyserService, StubAnalyserService>();
builder.Services.AddSingleton<IDiagnosticsRingBuffer, StubDiagnosticsRingBuffer>();
builder.Services.AddSingleton<IThemeService, StubThemeService>();

await builder.Build().RunAsync();
