using AkmlSql.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 047 (WEB-001, contracts/engine-pairing.md §2): a page hosted by IIS is static, so HEAD / cannot name its engine.
/// The installer publishes the engine's bridge port next to the bundle (akml-engine.json) and the page reads it.
/// </summary>
public sealed class PageEngineManifestTests
{
    [Fact]
    public async Task A_static_page_with_a_valid_manifest_names_the_engine_on_its_own_host()
    {
        var js = new Handler(detect: false, manifest: new PageEngineManifest { ManifestVersion = 1, BridgePort = 59417, Scheme = "ws" });
        var service = new PageEngine(js, new Navigation("http://localhost:59416/"));

        var result = await service.GetAsync();

        Assert.Equal(PageEngineKind.Engine, result.Kind);
        Assert.Equal(PageEngineSource.InstallManifest, result.Source);
        Assert.Equal(new PageEngineInfo("localhost", 59417, "ws"), result.Engine);
        Assert.Equal("http://localhost:59416/akml-engine.json", js.ManifestUrl);
        Assert.Same(result, await service.GetAsync());   // cached like any definitive answer
    }

    [Fact]
    public async Task A_LAN_page_takes_the_secure_scheme_from_the_manifest()
    {
        var js = new Handler(detect: false, manifest: new PageEngineManifest { ManifestVersion = 1, BridgePort = 59417, Scheme = "wss" });
        var result = await new PageEngine(js, new Navigation("https://sql-host/")).GetAsync();

        Assert.Equal(PageEngineKind.Engine, result.Kind);
        Assert.Equal(new PageEngineInfo("sql-host", 59417, "wss"), result.Engine);
    }

    public static IEnumerable<object?[]> InvalidManifests() => new[]
    {
        new object?[] { null },
        new object?[] { new PageEngineManifest { ManifestVersion = 1, BridgePort = 80, Scheme = "ws" } },
        new object?[] { new PageEngineManifest { ManifestVersion = 1, BridgePort = 70000, Scheme = "ws" } },
        new object?[] { new PageEngineManifest { ManifestVersion = 1, BridgePort = 59417, Scheme = "http" } },
        new object?[] { new PageEngineManifest { ManifestVersion = 2, BridgePort = 59417, Scheme = "ws" } },
        new object?[] { new PageEngineManifest { ManifestVersion = 1, BridgePort = 59417, Scheme = null } },
    };

    [Theory]
    [MemberData(nameof(InvalidManifests))]
    public async Task A_missing_or_invalid_manifest_leaves_the_page_static(PageEngineManifest? manifest)
    {
        var result = await new PageEngine(new Handler(detect: false, manifest), new Navigation("http://localhost:59416/")).GetAsync();

        Assert.Equal(PageEngineKind.Static, result.Kind);
        Assert.Null(result.Engine);
    }

    [Fact]
    public async Task A_page_the_engine_serves_never_reads_the_manifest()
    {
        var js = new Handler(detect: true, manifest: new PageEngineManifest { ManifestVersion = 1, BridgePort = 1234, Scheme = "ws" });
        var result = await new PageEngine(js, new Navigation("http://localhost:59417/")).GetAsync();

        Assert.Equal(PageEngineKind.Engine, result.Kind);
        Assert.Equal(PageEngineSource.ServedPage, result.Source);
        Assert.Equal(new PageEngineInfo("localhost", 59417, "ws"), result.Engine);
        Assert.Null(js.ManifestUrl);
    }

    [Fact]
    public async Task A_manifest_that_fails_to_load_leaves_the_page_static()
    {
        var js = new Handler(detect: false, manifest: null) { ManifestThrows = true };
        var result = await new PageEngine(js, new Navigation("http://localhost:59416/")).GetAsync();

        Assert.Equal(PageEngineKind.Static, result.Kind);
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation(string origin) => Initialize(origin, origin + "editor");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class Handler(bool detect, PageEngineManifest? manifest) : IJSRuntime, IJSObjectReference
    {
        internal string? ManifestUrl { get; private set; }
        internal bool ManifestThrows { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "import":
                    Assert.Equal("./js/akml-page-engine.js", Assert.Single(args!));
                    return ValueTask.FromResult((TValue)(object)this);
                case "detect":
                    return ValueTask.FromResult((TValue)(object)detect);
                case "readManifest":
                    ManifestUrl = (string)Assert.Single(args!)!;
                    if (ManifestThrows) throw new JSException("fetch failed");
                    return ValueTask.FromResult((TValue)(object?)manifest!);
                default:
                    throw new InvalidOperationException("Unexpected JS call " + identifier);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
