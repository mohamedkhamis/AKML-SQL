using System;
using System.IO;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 032 US8 (T052, J1/FR-029) — the campaign's single idempotency failure (FMTA-006):
/// JOIN line-break decisions OSCILLATED inside parenthesized bodies (CTE bodies, derived
/// tables). Pass 1 broke a bare `JOIN` (written as `INNER JOIN` by the explicit-join
/// rewrite) onto its own line with a stray multi-space run; pass 2 saw the modifier-prefixed
/// form and collapsed it back. Property: formatting the formatter's own output MUST be a
/// byte-identical no-op (per the T009 lesson: prove by property test, never golden regen).
/// </summary>
public class JoinInParensIdempotencyTests
{
    private const string Fmta006 =
        "-- chained CTEs three deep\n" +
        "with L1 as (select ProductID, SupplierID, Price from dbo.Products where Price is not null), " +
        "L2 as (select SupplierID, avg(Price) as AvgPrice from L1 group by SupplierID), " +
        "L3 as (select s.SupplierName, l.AvgPrice from dbo.Suppliers s join L2 l on s.SupplierID=l.SupplierID)\n" +
        "select * from L3 where AvgPrice>15 order by AvgPrice desc";

    [Theory]
    [InlineData(Fmta006)]
    [InlineData("with c as (select a from t1 join t2 on t1.id = t2.id) select * from c")]
    [InlineData("select * from (select x.a from x join y on x.id = y.id) d where d.a > 1")]
    [InlineData("with c1 as (select a from t1 left join t2 on t1.id = t2.id), " +
                "c2 as (select b from t3 inner join t4 on t3.id = t4.id) select * from c1 join c2 on c1.a = c2.b")]
    public void Formatting_is_idempotent_for_joins_inside_parens(string sql)
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(sql, profile);
        Assert.True(once.ValidationPassed, once.FormattedText);

        var twice = new FormatterPipeline().Format(once.FormattedText, profile);

        Assert.Equal(once.FormattedText, twice.FormattedText);
    }

    [Fact]
    public void Fmta006_join_inside_cte_body_stays_inline_with_clean_spacing()
    {
        // Convergence direction chosen by the blessed goldens (sp031-10-cte-columns):
        // joins inside frozen scopes (CTE bodies) stay INLINE — which also removes the
        // "INNER JOIN   L2" stray-spacing artifact the campaign flaged (finding 7).
        var text = new FormatterPipeline().Format(Fmta006, LoadDefaultStyle()).FormattedText;

        Assert.Contains("s INNER JOIN L2 l", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JOIN   L2", text, StringComparison.OrdinalIgnoreCase);
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
