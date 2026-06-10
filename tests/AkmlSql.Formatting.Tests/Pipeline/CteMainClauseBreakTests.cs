using System;
using System.IO;
using System.Text.RegularExpressions;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T009 — the main SELECT of a WITH (CTE) statement starts its own line instead of
/// cramming onto the CTE's closing paren (") SELECT …"). The clause tracker freezes inside the
/// parenthesised CTE body, so the main SELECT arrives with ClauseContext.With — the decider's
/// SELECT break previously fired only for ClauseContext.None and fell through to "single space".
/// </summary>
public class CteMainClauseBreakTests
{
    // CTE bodies are kept long enough that neither the whole-statement collapse nor the
    // short-list collapse folds the region — the assertions target the base-layout break.

    [Fact]
    public void Cte_MainSelect_StartsItsOwnLine()
    {
        const string sql =
            "with summary (region, total) as (" +
            "select region, sum(amount) as total from orders where status = 'Active' group by region" +
            ") select region, total from summary where total > 1000;";
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());

        Assert.DoesNotMatch(new Regex(@"\)[ \t]*SELECT\b", RegexOptions.IgnoreCase), result.FormattedText);
        Assert.Matches(new Regex(@"^\s*SELECT\s+region", RegexOptions.IgnoreCase | RegexOptions.Multiline), result.FormattedText);
        Assert.True(result.ValidationPassed);
    }

    [Fact]
    public void MultipleCtes_MainSelect_StartsItsOwnLine()
    {
        const string sql =
            "with active_customers as (" +
            "select customerid, customername from customers where status = 'Active'" +
            "), recent_orders as (" +
            "select orderid, customerid, total from orders where orderdate >= dateadd(month, -6, getdate())" +
            ") select c.customername, count(o.orderid) as order_count " +
            "from active_customers c left join recent_orders o on o.customerid = c.customerid " +
            "group by c.customername;";
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());

        Assert.DoesNotMatch(new Regex(@"\)[ \t]*SELECT\b", RegexOptions.IgnoreCase), result.FormattedText);
        Assert.True(result.ValidationPassed);
    }

    [Fact]
    public void Cte_MainSelect_Break_IsIdempotent()
    {
        const string sql =
            "with summary (region, total) as (" +
            "select region, sum(amount) as total from orders where status = 'Active' group by region" +
            ") select region, total from summary where total > 1000;";
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
