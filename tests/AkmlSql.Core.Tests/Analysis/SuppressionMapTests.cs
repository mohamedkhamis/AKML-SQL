using AkmlSql.Engine.Analysis;
using Xunit;

namespace AkmlSql.Core.Tests.Analysis;

public class SuppressionMapTests
{
    // ── Static Empty instance ─────────────────────────────────────────────────

    [Fact]
    public void Empty_NeverSuppressesAnything()
    {
        Assert.False(SuppressionMap.Empty.IsSuppressed(1, "PE001"));
        Assert.False(SuppressionMap.Empty.IsSuppressed(100, "SE002"));
    }

    // ── Per-line suppression (specific rule) ──────────────────────────────────

    [Fact]
    public void IsSuppressed_ReturnsTrue_WhenLineContainsSpecificRule()
    {
        var map = new SuppressionMap();
        map.SuppressedLines[5] = ["PE001", "BP004"];

        Assert.True(map.IsSuppressed(5, "PE001"));
        Assert.True(map.IsSuppressed(5, "BP004"));
    }

    [Fact]
    public void IsSuppressed_ReturnsFalse_WhenRuleNotInLineSet()
    {
        var map = new SuppressionMap();
        map.SuppressedLines[5] = ["PE001"];

        Assert.False(map.IsSuppressed(5, "SE001"));
    }

    [Fact]
    public void IsSuppressed_ReturnsFalse_WhenLineNotSuppressed()
    {
        var map = new SuppressionMap();
        map.SuppressedLines[5] = ["PE001"];

        Assert.False(map.IsSuppressed(6, "PE001"));
        Assert.False(map.IsSuppressed(4, "PE001"));
    }

    // ── Per-line suppression (all rules — null set) ───────────────────────────

    [Fact]
    public void IsSuppressed_ReturnsTrue_WhenNullSet_SuppressesAllRules()
    {
        var map = new SuppressionMap();
        map.SuppressedLines[10] = null; // null = suppress ALL rules on this line

        Assert.True(map.IsSuppressed(10, "PE001"));
        Assert.True(map.IsSuppressed(10, "SE002"));
        Assert.True(map.IsSuppressed(10, "ANY_RULE"));
    }

    // ── Block suppression ─────────────────────────────────────────────────────

    [Fact]
    public void IsSuppressed_ReturnsTrue_WhenLineInsideBlock()
    {
        var map = new SuppressionMap();
        map.SuppressedBlocks.Add((StartLine: 10, EndLine: 20));

        Assert.True(map.IsSuppressed(10, "PE001")); // inclusive start
        Assert.True(map.IsSuppressed(15, "SE001")); // middle
        Assert.True(map.IsSuppressed(20, "BP004")); // inclusive end
    }

    [Fact]
    public void IsSuppressed_ReturnsFalse_WhenLineOutsideBlock()
    {
        var map = new SuppressionMap();
        map.SuppressedBlocks.Add((StartLine: 10, EndLine: 20));

        Assert.False(map.IsSuppressed(9, "PE001"));
        Assert.False(map.IsSuppressed(21, "PE001"));
    }

    [Fact]
    public void IsSuppressed_ReturnsTrue_ForAnyRule_WhenInsideBlock()
    {
        var map = new SuppressionMap();
        map.SuppressedBlocks.Add((StartLine: 5, EndLine: 5));

        Assert.True(map.IsSuppressed(5, "ANYTHING"));
    }

    // ── Multiple blocks ───────────────────────────────────────────────────────

    [Fact]
    public void IsSuppressed_HandlesMultipleBlocks()
    {
        var map = new SuppressionMap();
        map.SuppressedBlocks.Add((1, 5));
        map.SuppressedBlocks.Add((15, 20));

        Assert.True(map.IsSuppressed(3, "PE001"));
        Assert.False(map.IsSuppressed(10, "PE001"));
        Assert.True(map.IsSuppressed(18, "PE001"));
    }

    // ── Block takes priority over line ────────────────────────────────────────

    [Fact]
    public void IsSuppressed_BlockTakesPriority_OverLineSuppression()
    {
        var map = new SuppressionMap();
        map.SuppressedBlocks.Add((1, 10));
        // Even without a line entry, block suppression wins
        Assert.True(map.IsSuppressed(5, "XYZ999"));
    }
}
