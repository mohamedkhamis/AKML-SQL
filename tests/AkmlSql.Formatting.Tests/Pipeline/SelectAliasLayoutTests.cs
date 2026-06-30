using System;
using System.IO;
using System.Text.RegularExpressions;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T009 — select-list items must not fragment one token per line ("COUNT(x)" ⏎ "AS" ⏎
/// "alias"). The clause tracker never leaves SelectPendingFirstItem until the next clause keyword
/// (its first-item handoff tests Select after the context already moved on), so the decider's
/// "first select item on a new line" break fired for EVERY unclassified select-list token — AS
/// keywords, aliases, operands after operators, and subquery internals. The break is now gated to
/// tokens that actually follow the SELECT header (plus a subquery's own SELECT after "(").
/// Select lists below are kept long enough that the short-list collapse cannot mask the layout.
/// </summary>
public class SelectAliasLayoutTests
{
    private static readonly Regex LoneAsLine = new(@"^\s*AS\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    [Fact]
    public void FunctionAlias_StaysWithItsItem()
    {
        const string sql =
            "select c.customername, count(o.orderid) as order_count, sum(o.total) as total_spent, " +
            "max(o.orderdate) as latest_order from customers c group by c.customername;";
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());

        Assert.DoesNotMatch(LoneAsLine, result.FormattedText);
        Assert.Matches(new Regex(@"\)\s+AS\s+order_count", RegexOptions.IgnoreCase), result.FormattedText);
        Assert.True(result.ValidationPassed);
    }

    [Fact]
    public void SubqueryAlias_StaysWithClosingParen()
    {
        const string sql =
            "select c.customerid, c.customername, " +
            "(select count(*) from orders o where o.customerid = c.customerid and o.total > 500) as order_count " +
            "from customers c order by c.customerid;";
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());

        Assert.DoesNotMatch(LoneAsLine, result.FormattedText);
        Assert.Matches(new Regex(@"\)\s+AS\s+order_count", RegexOptions.IgnoreCase), result.FormattedText);
    }

    [Fact]
    public void OperandsAfterOperators_DoNotFragment()
    {
        // Inside a select-list scalar subquery the clause tracker is frozen, so its WHERE operands
        // previously hit the same stray break ("o.customerid =" ⏎ "c.customerid" / "AND" ⏎ …).
        const string sql =
            "select c.customerid, c.customername, " +
            "(select sum(total) from orders o where o.customerid = c.customerid and " +
            "o.orderdate >= dateadd(year, -1, getdate())) as last_year_total " +
            "from customers c order by last_year_total desc;";
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());

        Assert.DoesNotMatch(new Regex(@"=\s*$", RegexOptions.Multiline), result.FormattedText);
        Assert.DoesNotMatch(new Regex(@"^\s*AND\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline), result.FormattedText);
    }

    [Fact]
    public void SelectList_Layout_IsIdempotent()
    {
        const string sql =
            "select c.customername, count(o.orderid) as order_count, sum(o.total) as total_spent, " +
            "max(o.orderdate) as latest_order from customers c group by c.customername;";
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(sql, profile).FormattedText;
        var twice = new FormatterPipeline().Format(once, profile).FormattedText;
        Assert.Equal(once, twice);
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
