using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T011 — commaPosition "leading" must actually produce leading commas. It never took
/// effect: ApplyCommaPosition ran FIRST in ListRules.Apply, before ApplyOneItemPerLine created the
/// per-item breaks it moves commas onto — so every list rendered trailing commas regardless of the
/// option. The select list below is kept over the collapse threshold so its items stay multi-line
/// (a collapsed list legitimately keeps inline commas).
/// </summary>
public class LeadingCommaLayoutTests
{
    private const string MultiItem =
        "select o.orderid, c.customername, sum(d.unitprice * d.quantity) as total_amount, " +
        "max(d.discount) as best_discount from orders o " +
        "inner join orderdetails d on d.orderid = o.orderid " +
        "inner join customers c on c.customerid = o.customerid group by o.orderid, c.customername;";

    [Fact]
    public void LeadingStyle_SelectItems_HaveLeadingCommas()
    {
        var result = new FormatterPipeline().Format(MultiItem, LoadStyle("leading-commas"));
        Assert.True(result.ValidationPassed, result.FormattedText);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.TrimEnd()).ToArray();

        // Items 2+ of the (non-collapsed) select list start with a leading comma…
        Assert.Contains(lines, l => l.TrimStart().StartsWith(", c.customername", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.TrimStart().StartsWith(", SUM", StringComparison.OrdinalIgnoreCase));

        // …and no select-item line ends with a trailing comma.
        Assert.DoesNotContain(lines, l => l.EndsWith(","));
    }

    [Fact]
    public void TrailingStyle_IsUnaffected()
    {
        var result = new FormatterPipeline().Format(MultiItem, LoadStyle("default"));
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).ToArray();
        Assert.DoesNotContain(lines, l => l.StartsWith(","));
    }

    [Fact]
    public void LeadingStyle_InlineCase_KeepsWhenIndentRelativeToItsLine()
    {
        // With leading commas the break sits on the comma and CASE is inline after it — the CASE
        // rules must derive caseIndent from the LINE (the comma's indent), not from the inline
        // CASE node's own zeroed IndentLevel, or WHEN/ELSE/END de-dent to the item level.
        const string sql =
            "select orderid, total, case when total > 1000 then 'Large' " +
            "when total > 100 then 'Medium' else 'Small' end as size_bucket from orders;";
        var result = new FormatterPipeline().Format(sql, LoadStyle("leading-commas"));
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n').ToArray();

        var caseLine = Array.Find(lines, l => l.TrimStart().StartsWith(", CASE", StringComparison.OrdinalIgnoreCase));
        var whenLine = Array.Find(lines, l => l.TrimStart().StartsWith("WHEN", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(caseLine);
        Assert.NotNull(whenLine);
        Assert.True(Indent(whenLine!) > Indent(caseLine!),
            $"WHEN must be indented deeper than its CASE's line:\n{result.FormattedText}");
    }

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    [Fact]
    public void LeadingStyle_IsIdempotent()
    {
        var profile = LoadStyle("leading-commas");
        var once = new FormatterPipeline().Format(MultiItem, profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
    }

    private static FormattingProfile LoadStyle(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("AKML-SQL.slnx not found");
        var stylePath = Path.Combine(dir.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", name + ".akmlstyle");
        return ProfileSerializer.Deserialize(File.ReadAllText(stylePath));
    }
}
