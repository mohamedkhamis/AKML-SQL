using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T023 (US1): how much per-release work one render of <c>/download</c> costs.
/// <para>
/// SC-003 says render cost must not vary materially between a manifest holding one release and one
/// holding a hundred. That is gated here by <b>counting probes</b>, not by wall-clock timing:
/// timing assertions in CI are flaky and end up muted, and this repo already carries a known-drifting
/// <c>PerformanceBaselineTests</c> as the cautionary example.
/// </para>
/// <para>
/// The specific regression being pinned: <c>PreviousReleases</c> used to be an expression-bodied
/// property evaluated twice per render — once for <c>.Count > 0</c> and again for the
/// <c>foreach</c> — so every release was probed on the filesystem twice.
/// </para>
/// </summary>
public sealed class DownloadPageProbeTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>A temp-file settings store pinned to one visibility choice.</summary>
    private SiteSettingsStore NewSettings(ReleaseVisibilityMode mode, int count = 3)
    {
        var store = new SiteSettingsStore(Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".db"));
        store.CreateTableIfMissing();
        store.Load();
        store.Save(store.Current with { Visibility = mode, VisibilityCount = count }, "test");
        return store;
    }

    /// <summary>Records every path the page asks about, and reports the file present or absent.</summary>
    private sealed class CountingProbe(bool filesExist)
    {
        public List<string> Probed { get; } = [];

        public string? Resolve(string relativePath)
        {
            Probed.Add(relativePath);
            return filesExist ? @"C:\downloads\" + relativePath : null;
        }
    }

    private static Release MakeRelease(int index, bool withCdn) => new()
    {
        Version = $"1.26.09{index:D2}.1200",
        ReleasedAt = new DateOnly(2026, 9, 10).AddDays(-index),
        SupportedHosts = ["SSMS 22", "VS 2026"],
        DownloadUrl = $"downloads/AKMLSQLSetup-{index}.exe",
        Sha256Hash = new string('a', 64),
        CdnUrl = withCdn
            ? $"https://github.com/mohamedkhamis/AKML-SQL/releases/download/v{index}/AKMLSQLSetup-{index}.exe"
            : null,
    };

    private static ReleasesManifest ManifestOf(int count, bool withCdn) =>
        ReleasesManifest.Create(
            Enumerable.Range(0, count).Select(i => MakeRelease(i, withCdn)).ToList(),
            product: "AKML SQL");

    private BunitContext NewCtx(
        ReleasesManifest manifest,
        CountingProbe probe,
        ReleaseVisibilityMode mode = ReleaseVisibilityMode.All,
        int count = 3)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(manifest);
        ctx.Services.AddSingleton(new ReleaseAvailability(@"C:\downloads", probe.Resolve));
        ctx.Services.AddSingleton(NewSettings(mode, count));
        return ctx;
    }

    [Fact]
    public void Render_ProbesEachLocalReleaseAtMostOnce()
    {
        // The regression guard. Before T021 this produced 2 probes per release.
        var probe = new CountingProbe(filesExist: true);
        using var ctx = NewCtx(ManifestOf(16, withCdn: false), probe);

        ctx.Render<Download>();

        // Contract R4.2: one availability probe per release, plus ONE size stat for the primary
        // card. 16 + 1 = 17. Before T021 this was ~32.
        Assert.Equal(17, probe.Probed.Count);
        Assert.Equal(16, probe.Probed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Render_WithAHundredLocalReleases_StillProbesEachOnlyOnce()
    {
        var probe = new CountingProbe(filesExist: true);
        using var ctx = NewCtx(ManifestOf(100, withCdn: false), probe);

        ctx.Render<Download>();

        // Linear in the number of releases, never quadratic and never doubled: 100 + the primary
        // card's size stat.
        Assert.Equal(101, probe.Probed.Count);
        Assert.Equal(100, probe.Probed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Render_WithCdnBackedReleases_ProbesOnlyThePrimaryCardsSize()
    {
        // This is the shape of the LIVE manifest: all 16 releases carry a GitHub mirror.
        var probe = new CountingProbe(filesExist: false);
        using var ctx = NewCtx(ManifestOf(100, withCdn: true), probe);

        var cut = ctx.Render<Download>();

        // Availability costs ZERO probes for all 100 (contract R1.1). The single remaining probe is
        // the primary card's size stat, which reads the local file when there is one (R1.4) — here
        // there is not, so the size row is simply omitted rather than guessed.
        Assert.Single(probe.Probed);

        // And crucially they are still OFFERED — the old rule hid every one of them the moment
        // the local file was absent, even though /dl redirects to a working CDN URL.
        Assert.Contains("Download AKML SQL", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Download size", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SizeIsResolvedOnceForThePrimaryCardOnly()
    {
        var probe = new CountingProbe(filesExist: true);
        using var ctx = NewCtx(ManifestOf(5, withCdn: false), probe);

        ctx.Render<Download>();

        // 5 availability probes. DisplaySize for the primary card reuses the same resolver, so the
        // newest release may be probed twice in total — but no release is probed more than that,
        // and the previous-releases list is never resolved twice.
        var perRelease = probe.Probed
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        Assert.All(perRelease.Values, count => Assert.True(count <= 2, $"probed {count} times"));
        Assert.Equal(1, perRelease.Values.Count(c => c == 2)); // only the primary card
    }
}
