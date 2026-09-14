using AkmlSql.Site.Releases;
using AkmlSql.Site.Settings;
using Xunit;

namespace AkmlSql.Site.Tests.Settings;

/// <summary>
/// Spec 038 T036 (US2): the visibility selection rule itself, independent of any page.
/// </summary>
public sealed class ReleaseVisibilityTests
{
    private static Release MakeRelease(int index) => new()
    {
        Version = $"1.26.0910.{1000 + index}",
        ReleasedAt = new DateOnly(2026, 9, 10).AddDays(-index),
        SupportedHosts = ["SSMS 22"],
        DownloadUrl = $"downloads/AKMLSQLSetup-{index}.exe",
        Sha256Hash = new string('a', 64),
    };

    private static IReadOnlyList<Release> NewestFirst(int count) =>
        Enumerable.Range(0, count).Select(MakeRelease).ToList();

    [Fact]
    public void LatestOnly_SelectsExactlyTheNewest()
    {
        var result = ReleaseVisibility.Apply(ReleaseVisibilityMode.LatestOnly, 99, NewestFirst(16));

        var only = Assert.Single(result);
        Assert.Equal("1.26.0910.1000", only.Version);
    }

    [Fact]
    public void LatestN_SelectsTheNewestNInOriginalOrder()
    {
        var result = ReleaseVisibility.Apply(ReleaseVisibilityMode.LatestN, 3, NewestFirst(16));

        Assert.Equal(3, result.Count);
        Assert.Equal(["1.26.0910.1000", "1.26.0910.1001", "1.26.0910.1002"], result.Select(r => r.Version));
    }

    [Fact]
    public void All_SelectsEverything()
    {
        Assert.Equal(16, ReleaseVisibility.Apply(ReleaseVisibilityMode.All, 3, NewestFirst(16)).Count);
    }

    [Fact]
    public void ACountLargerThanTheList_ReturnsAllOfThem_NotAnError()
    {
        // Contract R2.5.
        Assert.Equal(2, ReleaseVisibility.Apply(ReleaseVisibilityMode.LatestN, 50, NewestFirst(2)).Count);
    }

    [Theory]
    [InlineData(ReleaseVisibilityMode.LatestOnly)]
    [InlineData(ReleaseVisibilityMode.LatestN)]
    [InlineData(ReleaseVisibilityMode.All)]
    public void NoMode_EverReordersTheList(ReleaseVisibilityMode mode)
    {
        var source = NewestFirst(10);

        var result = ReleaseVisibility.Apply(mode, 5, source);

        // Contract R2.2: ReleasesManifest has already sorted newest-first and derived IsLatest from
        // position. Apply only ever selects a PREFIX; reordering here would desync IsLatest.
        Assert.Equal(source.Take(result.Count).Select(r => r.Version), result.Select(r => r.Version));
    }

    [Theory]
    [InlineData(ReleaseVisibilityMode.LatestOnly)]
    [InlineData(ReleaseVisibilityMode.LatestN)]
    [InlineData(ReleaseVisibilityMode.All)]
    public void AnEmptyOrNullManifest_ReturnsEmptyWithoutThrowing(ReleaseVisibilityMode mode)
    {
        Assert.Empty(ReleaseVisibility.Apply(mode, 3, []));
        Assert.Empty(ReleaseVisibility.Apply(mode, 3, null));
    }

    [Theory]
    [InlineData("LatestOnly", ReleaseVisibilityMode.LatestOnly)]
    [InlineData("latestn", ReleaseVisibilityMode.LatestN)]
    [InlineData("All", ReleaseVisibilityMode.All)]
    [InlineData("nonsense", ReleaseVisibilityBounds.DefaultMode)]
    [InlineData("", ReleaseVisibilityBounds.DefaultMode)]
    [InlineData(null, ReleaseVisibilityBounds.DefaultMode)]
    public void Parse_FallsBackToTheDefaultForAnythingUnrecognised(string? stored, ReleaseVisibilityMode expected)
    {
        Assert.Equal(expected, ReleaseVisibility.Parse(stored));
    }

    [Fact]
    public void Label_ReadsAsPlainLanguageForTheSettingsPage()
    {
        Assert.Equal("Latest release only", ReleaseVisibility.Label(ReleaseVisibilityMode.LatestOnly, 3));
        Assert.Equal("Latest 3 releases", ReleaseVisibility.Label(ReleaseVisibilityMode.LatestN, 3));
        Assert.Equal("Latest release only", ReleaseVisibility.Label(ReleaseVisibilityMode.LatestN, 1));
        Assert.Equal("All releases", ReleaseVisibility.Label(ReleaseVisibilityMode.All, 3));
    }

    [Fact]
    public void TheDefault_IsNotShowEverything()
    {
        // FR-016, asserted directly on the constant so a future edit cannot quietly regress it.
        Assert.NotEqual(ReleaseVisibilityMode.All, ReleaseVisibilityBounds.DefaultMode);
        Assert.InRange(ReleaseVisibilityBounds.DefaultCount, ReleaseVisibilityBounds.MinCount, ReleaseVisibilityBounds.MaxCount);
    }
}
