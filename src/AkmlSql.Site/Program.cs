using AkmlSql.Site.Admin;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components;
using AkmlSql.Site.Docs;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Seo;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();

// --- Composition root (spec 034) ---------------------------------------------
// Story services register here; nothing else belongs in Program.cs.
// Static SSR only -- no interactive Server/WebAssembly render modes are registered.

// T012 (US1): download page feed, loaded once from wwwroot/releases.json.
// Missing/invalid manifest resolves to the friendly-fallback state per contracts/releases-json.md.
builder.Services.AddSingleton(sp => ReleasesManifest.Load(sp.GetRequiredService<IWebHostEnvironment>()));

// T023 (US2): docs catalog + render + search index, built ONCE at startup and cached
// (contracts/docs-content.md: no per-request parsing). Config binding: "Docs" section.
builder.Services.Configure<DocsOptions>(builder.Configuration.GetSection(DocsOptions.SectionName));
builder.Services.AddSingleton(sp => DocsContentService.Build(
    sp.GetRequiredService<IWebHostEnvironment>(),
    sp.GetRequiredService<IOptions<DocsOptions>>().Value));

// T031 (SEO): canonical base URL for sitemap.xml (config section "Site", default https://akmlsql.com).
builder.Services.Configure<SiteOptions>(builder.Configuration.GetSection(SiteOptions.SectionName));

// Site metrics (analytics + admin portal): SQLite store as a singleton, fire-and-forget channel
// sink with a single background consumer, and cookie auth + login throttle for /admin.
builder.Services.Configure<AnalyticsOptions>(builder.Configuration.GetSection(AnalyticsOptions.SectionName));
builder.Services.Configure<DownloadsOptions>(builder.Configuration.GetSection(DownloadsOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.AddSingleton(sp => new AnalyticsStore(sp.GetRequiredService<IOptions<AnalyticsOptions>>().Value));
builder.Services.AddSingleton<ChannelAnalyticsSink>();
builder.Services.AddSingleton<IAnalyticsSink>(sp => sp.GetRequiredService<ChannelAnalyticsSink>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChannelAnalyticsSink>());
builder.Services.AddSingleton<AdminLoginThrottle>();

// Admin cookie: HTTPS-only, HttpOnly, SameSite=Lax, sliding 8-hour session. Visitors get no cookie.
builder.Services.AddAuthentication(AdminAuth.Scheme)
    .AddCookie(AdminAuth.Scheme, options =>
    {
        options.LoginPath = "/admin/login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Cookie.Name = "akml.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

var app = builder.Build();

// C5: both singletons are "startup-built" (the manifest parses wwwroot/releases.json; the
// docs service parses/renders the whole corpus). Resolve them eagerly so a broken deploy
// fails fast at boot instead of on the first request. The analytics store joins them: a
// missing/unwritable database folder must fail the deploy, not the first page view.
_ = app.Services.GetRequiredService<ReleasesManifest>();
_ = app.Services.GetRequiredService<DocsContentService>();
_ = app.Services.GetRequiredService<AnalyticsStore>();

// S2: security response headers on EVERY response (incl. error pages, so register first).
// Static SSR with no inline scripts/styles (the theme boot lives in js/theme-boot.js), so a
// strict CSP applies.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'");
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

// Auth before antiforgery and the /admin branch guard (the guard reads HttpContext.User).
app.UseAuthentication();
app.UseMiddleware<AdminBranchMiddleware>();

// Visit metrics: runs after routing decisions by wrapping the rest of the pipeline; it only
// observes the final response (2xx + text/html + public path) and enqueues fire-and-forget.
app.UseMiddleware<VisitTrackingMiddleware>();

app.UseAntiforgery();

// T023/T026 (US2): docs image assets are linked into Content/docs-assets/ by the csproj
// glob (T003) — images ONLY (no .md, no .svg), so nothing active/downloadable is exposed —
// outside wwwroot, so map them explicitly. Long cache: assets change only per deploy.
var docsOptions = app.Services.GetRequiredService<IOptions<DocsOptions>>().Value;
var docsAssetsRoot = DocsContentService.ResolveAssetsRootPath(app.Environment, docsOptions);
if (Directory.Exists(docsAssetsRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(docsAssetsRoot),
        RequestPath = DocsContentService.AssetsRequestPath,
        OnPrepareResponse = ctx =>
            ctx.Context.Response.Headers.CacheControl = "public, max-age=86400",
    });
}

// T027 (US2): startup-generated full-text search index (static-computed asset per contract).
// T031/T033: sitemap.xml over the static routes + every docs route. Both payloads change only
// per deploy, so they carry an hourly cache header (SC-004).
app.MapGet("/search-index.json", (HttpContext http, DocsContentService docs) =>
{
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Text(docs.SearchIndexJson, "application/json");
});

app.MapGet("/sitemap.xml", (HttpContext http, DocsContentService docs, IOptions<SiteOptions> site) =>
{
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Text(Sitemap.Build(site.Value.BaseUrl, docs.Documents), "application/xml");
});

app.MapStaticAssets();

// Tracked installer downloads + admin portal POST endpoints (login/logout).
DownloadEndpoint.Map(app);
AdminEndpoints.Map(app);

app.MapRazorComponents<App>();

app.Run();
