using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T011 — list.alignAliases must actually align the AS keywords of a multi-line select
/// list to one column. It was inert: ApplyAlignAliases ran inside ListRules, BEFORE
/// ParenthesisRules re-joined the exploded function-call parens, so every AS line still started
/// at the lone ")" — all measured widths were equal and the computed padding was always one
/// space. Alignment is geometry, so it runs at the FormatterPipeline post-collapse finalization
/// (the same chokepoint as the spacing normalizers), where the final line shapes exist.
/// </summary>
public class AliasAlignmentTests
{
    private const string MultiAlias =
        "select c.customername, count(o.orderid) as order_count, sum(o.total) as total_spent, " +
        "max(o.orderdate) as latest_order from customers c group by c.customername;";

    [Fact]
    public void SelectAliases_AlignToOneColumn()
    {
        var result = new FormatterPipeline().Format(MultiAlias, LoadDefaultStyle());
        Assert.True(result.ValidationPassed, result.FormattedText);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');

        var asColumns = lines
            .Where(l => l.Contains(" AS ", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Equal(3, asColumns.Length);
        Assert.True(asColumns.Distinct().Count() == 1,
            $"AS keywords not aligned (columns: {string.Join(",", asColumns)}):\n{result.FormattedText}");

        // And the alignment actually padded the shorter item (SUM(o.total) is 4 chars shorter
        // than COUNT(o.orderid)) — not just trivially equal one-space columns.
        Assert.Contains(lines, l =>
            l.Contains("SUM(o.total)", StringComparison.OrdinalIgnoreCase) &&
            l.Contains("  AS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectAliases_Alignment_IsIdempotent()
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(MultiAlias, profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
    }

    [Fact]
    public void SingleAlias_IsNotPadded()
    {
        // Fewer than two aliases — nothing to align; the lone AS keeps its single space.
        const string sql =
            "select o.orderid, c.customername, sum(d.unitprice * d.quantity) as total " +
            "from orders o inner join orderdetails d on d.orderid = o.orderid " +
            "inner join customers c on c.customerid = o.customerid group by o.orderid, c.customername;";
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());
        Assert.Contains("quantity) AS total", result.FormattedText, StringComparison.OrdinalIgnoreCase);
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
