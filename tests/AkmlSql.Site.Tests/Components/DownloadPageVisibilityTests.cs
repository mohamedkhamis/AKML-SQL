using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Releases;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T037 (US2): the release-visibility setting as the public page applies it.
/// <para>
/// Two things are pinned here. The obvious one is that the owner's choice governs what is
/// advertised. The less obvious one matters more: the choice is applied <b>before</b> any
/// availability probe, so per-render work follows what is shown rather than what is published.
/// That is what makes SC-003 hold for a manifest of any size (contract R2.1).
/// </para>
/// </summary>
public sealed class DownloadPageVisibilityTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class CountingProbe
    {
        public List<string> Probed { get; } = [];

        public string? Resolve(string relativePath)
        {
            Probed.Add(relativePath);
            return @"C:\downloads\" + relativePath; // always present
        }
    }

    private static Release MakeRelease(int index) => new()
    {
        Version = $"1.26.0910.{1000 + index}",
        ReleasedAt = new DateOnly(2026, 9, 10).AddDays(-index),
        SupportedHosts = ["SSMS 22"],
        DownloadUrl = $"downloads/AKMLSQLSetup-{index}.exe",
        Sha256Hash = new string('a', 64),
    };

    private static ReleasesManifest ManifestOf(int count) =>
        ReleasesManifest.Create(Enumerable.Range(0, count).Select(MakeRelease).ToList(), product: "AKML SQL");

    private SiteSettingsStore NewSettings(ReleaseVisibilityMode mode, int count = 3)
    {
        var store = new SiteSettingsStore(Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".db"));
        store.CreateTableIfMissing();
        store.Load();
        if (mode != ReleaseVisibilityBounds.DefaultMode || count != ReleaseVisibilityBounds.DefaultCount)
        {
            store.Save(store.Current with { Visibility = mode, VisibilityCount = count }, "test");
        }

        return store;
    }

    private BunitContext NewCtx(ReleasesManifest manifest, SiteSettingsStore settings, CountingProbe probe)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton(manifest);
        ctx.Services.AddSingleton(new ReleaseAvailability(@"C:\downloads", probe.Resolve));
        ctx.Services.AddSingleton(settings);
        return ctx;
    }

    [Fact]
    public void LatestOnly_OffersExactlyOneReleaseAndRendersNoHistorySection()
    {
        var probe = new CountingProbe();
        using var ctx = NewCtx(ManifestOf(16), NewSettings(ReleaseVisibilityMode.LatestOnly), probe);

        var cut = ctx.Render<Download>();

        // Contract R2.2: the history section is not rendered AT ALL, not rendered empty.
        Assert.Empty(cut.FindAll(".release-history"));
        Assert.Single(cut.FindAll(".release-card"));
        Assert.Contains("1.26.0910.1000", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("1.26.0910.1001", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LatestOnly_WithAHundredReleases_CostsAtMostOneProbe()
    {
        var probe = new CountingProbe();
        using var ctx = NewCtx(ManifestOf(100), NewSettings(ReleaseVisibilityMode.LatestOnly), probe);

        ctx.Render<Download>();

        // Contract R4.3 / SC-003. The primary card is probed for availability and for its size, so
        // the ceiling is one RELEASE, touched twice -- never the other 99.
        Assert.Single(probe.Probed.Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.True(probe.Probed.Count <= 2, $"expected at most 2 probes, saw {probe.Probed.Count}");
    }

    [Fact]
    public void LatestN_OffersTheNewestNAndProbesOnlyThose()
    {
        var probe = new CountingProbe();
        using var ctx = NewCtx(ManifestOf(100), NewSettings(ReleaseVisibilityMode.LatestN, 3), probe);

        var cut = ctx.Render<Download>();

        Assert.Equal(3, cut.FindAll(".release-card").Count);
        Assert.Equal(3, probe.Probed.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("1.26.0910.1003", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void All_OffersEveryDownloadableRelease()
    {
        var probe = new CountingProbe();
        using var ctx = NewCtx(ManifestOf(5), NewSettings(ReleaseVisibilityMode.All), probe);

        var cut = ctx.Render<Download>();

        Assert.Equal(5, cut.FindAll(".release-card").Count);
    }

    [Fact]
    public void WithNothingSaved_TheDefaultIsLatestThree_NotEverything()
    {
        var probe = new CountingProbe();
        var settings = new SiteSettingsStore(Path.Combine(_dir.Path, "empty.db"));
        settings.CreateTableIfMissing();
        settings.Load(); // nothing saved

        using var ctx = NewCtx(ManifestOf(16), settings, probe);

        var cut = ctx.Render<Download>();

        // FR-016: the shipped default must not be "show every release" -- the 16-entry wall is the
        // defect this feature exists to fix, so it must not be what a fresh install produces.
        Assert.Equal(3, cut.FindAll(".release-card").Count);
    }

    [Fact]
    public void NExceedingTheManifest_ShowsAllOfThemWithoutError()
    {
        var probe = new CountingProbe();
        using var ctx = NewCtx(ManifestOf(2), NewSettings(ReleaseVisibilityMode.LatestN, 50), probe);

        var cut = ctx.Render<Download>();

        // Contract R2.5: not an error, just fewer releases than the ceiling.
        Assert.Equal(2, cut.FindAll(".release-card").Count);
    }

    [Fact]
    public void AnUnreadableSettingsStore_StillServesThePageOnTheDocumentedDefault()
    {
        var probe = new CountingProbe();

        // No CreateTableIfMissing: Load() fails and falls back rather than throwing.
        var settings = new SiteSettingsStore(Path.Combine(_dir.Path, "broken.db"));
        settings.Load();
        Assert.True(settings.LoadFailed);

        using var ctx = NewCtx(ManifestOf(16), settings, probe);

        var cut = ctx.Render<Download>();

        // FR-016a / SC-020: the primary call to action must never break for a settings problem.
        Assert.Equal(3, cut.FindAll(".release-card").Count);
        Assert.Contains("Download AKML SQL", cut.Markup, StringComparison.Ordinal);
    }
}
