using AkmlSql.Formatting.Pipeline;
using Xunit;

namespace AkmlSql.Core.Tests.Formatting;

public class NoformatScannerTests
{
    private readonly NoformatScanner _scanner = new();

    // ── Empty / null input ────────────────────────────────────────────────────

    [Fact]
    public void Scan_ReturnsEmpty_ForEmptyString()
    {
        Assert.Empty(_scanner.Scan(""));
    }

    [Fact]
    public void Scan_ReturnsEmpty_WhenNoMarkers()
    {
        Assert.Empty(_scanner.Scan("SELECT 1 FROM dbo.Orders"));
    }

    // ── Line comment markers ──────────────────────────────────────────────────

    [Fact]
    public void Scan_DetectsLineCommentNoformatRegion()
    {
        var sql = "SELECT 1\n-- noformat\nSELECT 2\n-- endnoformat\nSELECT 3";
        var regions = _scanner.Scan(sql);
        Assert.Single(regions);
    }

    [Fact]
    public void Scan_LineComment_RegionStartsAtNoformatTag()
    {
        var sql = "SELECT 1\n-- noformat\nSELECT 2\n-- endnoformat\nSELECT 3";
        var regions = _scanner.Scan(sql);
        // Region start should be at "-- noformat"
        Assert.True(regions[0].StartOffset >= "SELECT 1\n".Length);
    }

    [Fact]
    public void Scan_LineComment_WithHyphens_AndSpaces()
    {
        var sql = "--noformat\nSELECT 1\n-- endnoformat";
        var regions = _scanner.Scan(sql);
        Assert.Single(regions);
    }

    // ── Block comment markers ─────────────────────────────────────────────────

    [Fact]
    public void Scan_DetectsBlockCommentNoformatRegion()
    {
        var sql = "SELECT 1\n/* noformat */\nSELECT 2\n/* endnoformat */\nSELECT 3";
        var regions = _scanner.Scan(sql);
        Assert.Single(regions);
    }

    // ── Unmatched open tag (extends to EOF) ───────────────────────────────────

    [Fact]
    public void Scan_UnmatchedOpen_ExtendsToEndOfFile()
    {
        var sql = "SELECT 1\n-- noformat\nSELECT 2";
        var regions = _scanner.Scan(sql);
        Assert.Single(regions);
        Assert.False(regions[0].HasClosingTag);
        Assert.Equal(sql.Length, regions[0].EndOffset);
    }

    // ── Multiple regions ──────────────────────────────────────────────────────

    [Fact]
    public void Scan_MultipleRegions_ReturnsAll()
    {
        var sql = "-- noformat\nA\n-- endnoformat\nB\n-- noformat\nC\n-- endnoformat";
        var regions = _scanner.Scan(sql);
        Assert.Equal(2, regions.Count);
    }

    // ── IsInNoformatRegion ────────────────────────────────────────────────────

    [Fact]
    public void IsInNoformatRegion_ReturnsFalse_WhenNoRegions()
    {
        Assert.False(NoformatScanner.IsInNoformatRegion([], 0));
    }

    [Fact]
    public void IsInNoformatRegion_ReturnsTrue_WhenOffsetInsideRegion()
    {
        var sql = "AAA-- noformat\nBBB\n-- endnoformat\nCCC";
        var regions = _scanner.Scan(sql);
        Assert.True(regions.Count > 0);

        // "BBB" is inside the region
        int bbbOffset = sql.IndexOf("BBB");
        Assert.True(NoformatScanner.IsInNoformatRegion(regions, bbbOffset));
    }

    [Fact]
    public void IsInNoformatRegion_ReturnsFalse_WhenOffsetBeforeRegion()
    {
        var sql = "BEFORE\n-- noformat\nINSIDE\n-- endnoformat";
        var regions = _scanner.Scan(sql);

        Assert.False(NoformatScanner.IsInNoformatRegion(regions, 0));
    }

    [Fact]
    public void IsInNoformatRegion_ReturnsFalse_WhenOffsetAfterRegion()
    {
        var sql = "-- noformat\nINSIDE\n-- endnoformat\nAFTER";
        var regions = _scanner.Scan(sql);
        int afterOffset = sql.IndexOf("AFTER");

        Assert.False(NoformatScanner.IsInNoformatRegion(regions, afterOffset));
    }

    // ── HasClosingTag ─────────────────────────────────────────────────────────

    [Fact]
    public void Scan_Region_HasClosingTag_WhenEndnoformatPresent()
    {
        var sql = "-- noformat\nSELECT 1\n-- endnoformat";
        var regions = _scanner.Scan(sql);
        Assert.Single(regions);
        Assert.True(regions[0].HasClosingTag);
    }

    [Fact]
    public void Scan_Region_NoClosingTag_WhenEndnoformatAbsent()
    {
        var sql = "-- noformat\nSELECT 1";
        var regions = _scanner.Scan(sql);
        Assert.Single(regions);
        Assert.False(regions[0].HasClosingTag);
    }
}
