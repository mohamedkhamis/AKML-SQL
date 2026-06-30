using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T009 residual — when a short CTE body collapses onto one line, the CTE's closing ')'
/// (and the next CTE's header) must NOT be merged onto the body line
/// ("… WHERE active = 1), recent_orders AS ("). Root cause: <c>DmlRules.FindStatementEnd</c> was
/// not paren-depth aware — a CTE body's SELECT starts a "statement" whose range ran past the CTE's
/// enclosing ')' to the next break-carrying statement start, so <c>CollapseRange</c> deleted the
/// ')''s break. Same enclosing-paren boundary family as the <c>ListRules.FindListEnd</c> fix.
/// The body below sits between the subquery-collapse threshold (60, so the body is not legally
/// inlined into its parens) and the statement-collapse threshold (80, so the range collapse fires).
/// </summary>
public class CteBodyCollapseBoundaryTests
{
    private const string TwoCtes =
        "with a as (select customerid, customername from customers where active = 1), " +
        "b as (select orderid from orders) " +
        "select a.customername, b.orderid from a join b on a.customerid = b.orderid " +
        "where a.customername like 'X%' order by b.orderid desc;";

    [Fact]
    public void ShortCteBody_DoesNotMergeNextCteHeader()
    {
        var result = new FormatterPipeline().Format(TwoCtes, LoadDefaultStyle());
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).ToArray();

        // The first CTE's body line must not also carry the second CTE's header.
        Assert.False(lines.Any(l =>
                l.Contains("active = 1", StringComparison.OrdinalIgnoreCase) &&
                l.Contains("b AS", StringComparison.OrdinalIgnoreCase)),
            "CTE body line carries the next CTE's header:\n" + result.FormattedText);
        Assert.True(result.ValidationPassed, result.FormattedText);
    }

    [Fact]
    public void ShortCteBody_Collapse_IsIdempotent()
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(TwoCtes, profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
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
