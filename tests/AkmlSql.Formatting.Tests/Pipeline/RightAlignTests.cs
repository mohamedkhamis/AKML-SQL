using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T013 — true right-alignment for <c>operators.alignment: rightAligned</c> and
/// <c>inStatements.alignment: rightAligned</c> (both were faked: operators bumped indent, IN
/// stayed stacked — spec-020 GapToImplement). Real right-alignment needs per-space columns the
/// tab grid can't hit, so it runs as a finalization pass (<c>RightAligner</c>) using a new opt-in
/// <c>LayoutNode.AbsoluteLeadingSpaces</c> field that the emitter honors for line-start tokens
/// (spaces mode only — tabs can't sub-align). Pinned by the geometric invariant: the aligned
/// tokens' right edges land on one column. No built-in style uses rightAligned, so these focused
/// column-equality tests are the oracle.
/// </summary>
public class RightAlignTests
{
    [Fact]
    public void Operators_RightAligned_AndOr_ShareRightEdge()
    {
        var profile = LoadDefaultStyle();   // andOrNewLine "before" → AND/OR on own lines
        profile.Operators.Alignment = "rightAligned";
        const string sql = "select orderid, total from orders where orderdate between '2025-01-01' and '2025-12-31' and total between 100 and 10000 or status = 'Open';";
        var result = new FormatterPipeline().Format(sql, profile);
        Assert.True(result.ValidationPassed, result.FormattedText);

        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');
        int andEnd = RightEdgeOf(lines, "AND");
        int orEnd = RightEdgeOf(lines, "OR");
        Assert.True(andEnd > 0 && orEnd > 0, "AND/OR must each be on their own line:\n" + result.FormattedText);
        Assert.True(andEnd == orEnd,
            $"AND right-edge col {andEnd} != OR right-edge col {orEnd}:\n{result.FormattedText}");
    }

    [Fact]
    public void Operators_RightAligned_IsIdempotent()
    {
        var profile = LoadDefaultStyle();
        profile.Operators.Alignment = "rightAligned";
        const string sql = "select orderid, total from orders where orderdate between '2025-01-01' and '2025-12-31' and total between 100 and 10000 or status = 'Open';";
        var once = new FormatterPipeline().Format(sql, profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
    }

    [Fact]
    public void InItems_RightAligned_ShareRightEdge()
    {
        var profile = LoadDefaultStyle();
        profile.InStatements.Alignment = "rightAligned";
        profile.InStatements.PlaceItemsOnNewLine = "always";   // force the list multi-line
        const string sql = "select * from t where x in ('A', 'BB', 'CCC');";
        var result = new FormatterPipeline().Format(sql, profile);
        Assert.True(result.ValidationPassed, result.FormattedText);

        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');
        // Each value sits on its own line; right-justified, so the values' right edges align.
        int a = ValueRightEdge(lines, "'A'");
        int bb = ValueRightEdge(lines, "'BB'");
        int ccc = ValueRightEdge(lines, "'CCC'");
        Assert.True(a > 0 && bb > 0 && ccc > 0, "each IN value on its own line:\n" + result.FormattedText);
        Assert.True(a == bb && bb == ccc,
            $"IN value right-edges differ ({a}/{bb}/{ccc}):\n{result.FormattedText}");
    }

    [Fact]
    public void RightAligned_TabsMode_NoOp_StillValid()
    {
        // Tabs can't sub-align — right-alignment must degrade gracefully (no crash, valid output).
        var profile = LoadDefaultStyle();
        profile.Whitespace.TabStyle = "tabs";
        profile.Operators.Alignment = "rightAligned";
        const string sql = "select orderid, total from orders where orderdate between '2025-01-01' and '2025-12-31' and total between 100 and 10000 or status = 'Open';";
        var result = new FormatterPipeline().Format(sql, profile);
        Assert.True(result.ValidationPassed, result.FormattedText);
    }

    // The right edge (1-based end column) of a keyword that begins its line, or -1.
    private static int RightEdgeOf(string[] lines, string keyword)
    {
        foreach (var l in lines)
        {
            if (l.TrimStart().StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase)
                || l.Trim().Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                int idx = l.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                return idx + keyword.Length;
            }
        }
        return -1;
    }

    private static int ValueRightEdge(string[] lines, string value)
    {
        foreach (var l in lines)
        {
            int idx = l.IndexOf(value, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && l.TrimStart().StartsWith(value, StringComparison.OrdinalIgnoreCase))
                return idx + value.Length;
        }
        return -1;
    }

    private static FormattingProfile LoadDefaultStyle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("AKML-SQL.slnx not found");
        var stylePath = Path.Combine(dir.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "default.akmlstyle");
        return ProfileSerializer.Deserialize(File.ReadAllText(stylePath));
    }
}
