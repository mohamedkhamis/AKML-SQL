using AkmlSql.Site.Admin;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components;
using AkmlSql.Site.Docs;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Seo;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

// OPS-001/SEC-001: `AkmlSql.Site --hash-password` generates the value for the server's
// Admin__PasswordHash environment variable. Deliberately part of this executable rather than a
// side script, so the hash is always produced by the exact code path that verifies it.
if (args.Contains("--hash-password", StringComparer.OrdinalIgnoreCase))
{
    return AdminPasswordTool.Run();
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();

// PERF-001: MapStaticAssets pre-compresses css/js at build time, but everything the app generates
// itself -- every SSR page, search-index.json, sitemap.xml -- was going out raw (a docs page was
// 35 KB uncompressed). Compression is enabled for HTTPS too: the site serves no attacker-injected
// content reflected into responses, so the BREACH precondition does not apply here.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
        ["application/json", "application/xml", "image/svg+xml"]);
});

// --- Composition root (spec 034) ---------------------------------------------
// Story services register here; nothing else belongs in Program.cs.
// Static SSR only -- no interactive Server/WebAssembly render modes are registered.

// T012 (US1): download page feed, loaded once from wwwroot/releases.json.
// Missing/invalid manifest resolves to the friendly-fallback state per contracts/releases-json.md.
builder.Services.AddSingleton(sp => ReleasesManifest.Load(sp.GetRequiredService<IWebHostEnvironment>()));

// DL-001: the download page checks the advertised installer is actually on disk before offering
// it. Not a singleton snapshot -- files are dropped into the folder between deploys, so presence
// is resolved per render.
builder.Services.AddScoped(sp => new ReleaseAvailability(
    sp.GetRequiredService<IOptions<DownloadsOptions>>().Value.Folder));

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

// Offline IP-to-location lookup. The .mmdb is supplied by the deploy (scripts/update-geoip.ps1),
// not source control -- GeoLite2 needs a MaxMind licence key. Without the file every lookup
// returns "unknown" and the site behaves exactly as before, so geo is an enrichment, never a
// dependency. Singleton: the database is memory-mapped once and queried per request.
builder.Services.AddSingleton(sp => new GeoLookup(
    sp.GetRequiredService<IOptions<AnalyticsOptions>>().Value.GeoDatabasePath,
    sp.GetRequiredService<ILogger<GeoLookup>>()));

// ADM-006: client IP comes from Connection.RemoteIpAddress, which is correct on IIS in-process
// but becomes the PROXY's address the moment anything fronts the site (Cloudflare, ARR, a load
// balancer) -- at which point every visitor hashes to one value and unique-visitor counting
// silently becomes meaningless. Configured here so that day needs a config change, not a code
// change. KnownProxies/KnownNetworks are cleared and repopulated from config: with none listed
// the middleware trusts nothing and behaviour is exactly as before.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();

    foreach (var entry in builder.Configuration.GetSection("Analytics:KnownProxies").Get<string[]>() ?? [])
    {
        if (System.Net.IPAddress.TryParse(entry, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }
});
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
var analyticsStore = app.Services.GetRequiredService<AnalyticsStore>();

// ADM-004: retention prune at startup. The visits/downloads tables previously grew without bound.
// Once per boot rather than on a timer: the tables gain a few thousand rows a day at most, so a
// prune per deploy or app-pool recycle is ample, and it cannot interfere with a live request.
var analyticsOptions = app.Services.GetRequiredService<IOptions<AnalyticsOptions>>().Value;
var prunedRows = analyticsStore.Prune(analyticsOptions.RetentionDays);
if (prunedRows > 0)
{
    app.Logger.LogInformation(
        "Analytics retention: pruned {Rows} row(s) older than {Days} days.",
        prunedRows, analyticsOptions.RetentionDays);
}

// Repair history written before same-origin referrers were filtered at write time: internal
// navigation had made the site its own top referrer. Only the referrer columns are cleared, never
// a row, and the operation is idempotent — after the first run it corrects nothing.
var siteHost = Uri.TryCreate(
    app.Services.GetRequiredService<IOptions<SiteOptions>>().Value.CanonicalRoot,
    UriKind.Absolute,
    out var canonicalUri)
        ? canonicalUri.Host
        : null;
var correctedReferrers = analyticsStore.ClearSameOriginReferrers(siteHost);
if (correctedReferrers > 0)
{
    app.Logger.LogInformation(
        "Analytics: cleared self-referrer on {Rows} historical row(s) for host {Host}.",
        correctedReferrers, siteHost);
}

// PERF-001: compress the responses the app GENERATES -- SSR pages, search-index.json,
// sitemap.xml, robots.txt -- which were previously served raw (a docs page was 35 KB).
//
// Deliberately NOT applied to the static-asset roots: MapStaticAssets already serves those from
// variants compressed at BUILD time (better ratios than a per-request pass, and no CPU cost per
// request), negotiated via its own Content-Encoding selectors. Running this middleware over them
// would at best duplicate that work.
//
// Note for anyone verifying compression locally: under `dotnet run` the .gz/.br variants are not
// materialised next to wwwroot, so a gzip-accepting request for a static asset returns an empty
// body. That is a run-from-source artifact, NOT a deployment problem -- `dotnet publish` writes
// site.css.gz/.br alongside site.css and the published app negotiates them correctly. Verify
// static-asset compression against a published build, never against `dotnet run`.
//
// The excluded prefixes mirror VisitTrackingMiddleware.ExcludedPrefixes; both describe the same
// thing (paths that are assets rather than pages).
string[] staticAssetPrefixes = ["/css", "/js", "/img", "/lib", "/_framework", "/_content", "/docs-assets", "/favicon"];
app.UseWhen(
    context => !staticAssetPrefixes.Any(prefix =>
        context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)),
    branch => branch.UseResponseCompression());

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

// ADM-006: resolve the real client IP before anything reads it (visit tracking, the login
// throttle). No-op until Analytics:KnownProxies names a trusted proxy.
app.UseForwardedHeaders();

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

// OPS-004: liveness + startup-state probe for the deploy smoke test and IIS monitoring. Reports
// which startup singleton is unhealthy instead of just proving a page renders; never cached.
app.MapGet("/health", (
    HttpContext http,
    DocsContentService docs,
    ReleasesManifest releases,
    AnalyticsStore analytics,
    IOptions<AdminOptions> admin) =>
{
    http.Response.Headers.CacheControl = "no-store";

    var report = new
    {
        status = docs.IsEmpty || !releases.IsAvailable ? "degraded" : "ok",
        docs = docs.Documents.Count,
        releases = releases.Releases.Count,
        latestVersion = releases.Latest?.Version,
        analyticsDatabase = File.Exists(analytics.DatabasePath),
        adminConfigured = admin.Value.IsConfigured,
    };

    // Degraded is still a 200: the site serves fine without a release manifest, and a probe that
    // fails the deploy for a missing installer would block publishing the fix for it.
    return Results.Json(report);
});

app.MapGet("/sitemap.xml", (HttpContext http, DocsContentService docs, IOptions<SiteOptions> site) =>
{
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Text(Sitemap.Build(site.Value.BaseUrl, docs.Documents), "application/xml");
});

// SEO-002: generated, not a static file — the checked-in copy advertised a sitemap on a host the
// site does not serve, and nothing could catch the drift. Registered before MapStaticAssets so it
// wins over any stale wwwroot/robots.txt left behind by an older deploy.
app.MapGet("/robots.txt", (HttpContext http, IOptions<SiteOptions> site) =>
{
    http.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Text(RobotsTxt.Build(site.Value.BaseUrl), "text/plain");
});

app.MapStaticAssets();

// Tracked installer downloads + admin portal POST endpoints (login/logout).
DownloadEndpoint.Map(app);
DownloadEndpoint.MapCount(app);
AdminEndpoints.Map(app);

app.MapRazorComponents<App>();

app.Run();

// Explicit exit code: the --hash-password branch above makes this entry point int-returning.
return 0;
