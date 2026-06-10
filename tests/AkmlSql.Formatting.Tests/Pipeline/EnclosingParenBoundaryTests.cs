using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T009 — CTE / subquery boundary. <c>ListRules.FindListEnd</c> is now paren-depth aware:
/// a ')' that closes a paren opened BEFORE the list (a structural CTE / subquery / derived-table
/// close) ends the list, so <c>CollapseRange</c> no longer deletes that ')'s line break and merges
/// it — and the following clause — up. Previously a CTE body's last clause over-ran the closing ')'
/// (<c>… region) SELECT …</c>) and a scalar subquery's inner WHERE over-ran its ')'. A balanced
/// function-call ')' inside the list is still kept in the list (not a boundary).
/// </summary>
public class EnclosingParenBoundaryTests
{
    private static string[] Format(string sql)
        => new FormatterPipeline().Format(sql, LoadDefaultStyle())
            .FormattedText.Replace("\r\n", "\n").Split('\n');

    // CTE/subquery bodies are kept long enough (> the ~80-char collapse threshold) that the
    // collapse-short pass does not fold the body onto one line — so the assertions target the
    // structural-boundary merge, not legitimate short-list collapse.

    [Fact]
    public void Cte_MainSelect_NotMerged_OntoCteBodyClause()
    {
        const string sql =
            "with summary (region, total) as (" +
            "select region, sum(amount) as total from orders where status = 'Active' group by region" +
            ") select region, total from summary where total > 1000;";
        var lines = Format(sql);

        // The CTE body's GROUP BY line must not also carry the main query's SELECT (the "…region) "
        // "SELECT …" merge). With a non-collapsing body the body's own SELECT is on a different line.
        Assert.DoesNotContain(lines, l =>
        {
            var u = l.ToUpperInvariant();
            return u.Contains("GROUP BY") && u.Contains("SELECT ");
        });
        Assert.True(new FormatterPipeline().Format(sql, LoadDefaultStyle()).ValidationPassed);
    }

    [Fact]
    public void Subquery_CloseParen_NotMerged_OntoInnerWhere()
    {
        const string sql =
            "select c.id, (" +
            "select count(*) from orders o where o.customerid = c.id and o.total > 500 and o.status = 'X'" +
            ") as cnt from customers c order by c.id;";
        var lines = Format(sql);

        // The inner subquery's WHERE line must not also carry the closing ')' + the alias.
        Assert.DoesNotContain(lines, l =>
        {
            var u = l.ToUpperInvariant();
            return u.Contains("WHERE") && u.Contains(") AS ");
        });
    }

    [Fact]
    public void BalancedFunctionCallParen_StaysInList()
    {
        // A function call ')' inside a SELECT list is balanced — it must NOT split the list; the two
        // items stay collapsible on one line.
        var lines = Format("select sum(a), count(b) from t;");
        Assert.Contains(lines, l =>
        {
            var u = l.ToUpperInvariant();
            return u.Contains("SUM(A)") && u.Contains("COUNT(B)");
        });
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
