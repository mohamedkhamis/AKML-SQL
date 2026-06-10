using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T010 residual — the JOIN ON-condition must honor the style's
/// <c>join.onConditionNewLine</c> (all six built-in styles set it true). The base layout breaks
/// before ON, but <c>ListRules.ApplyCollapseShortLists</c> collapsed the JOIN's body list across it
/// (ON was not a list boundary), deleting the break — ON rendered inline. ON cannot be a UNIVERSAL
/// boundary (a list starting after a MERGE's ON pulls the following WHEN up — the 12-merge
/// regression), so the boundary is scoped: a list opened by JOIN stops at ON.
/// </summary>
public class JoinOnConditionLayoutTests
{
    private const string MultiJoin =
        "select o.id, c.name from orders o " +
        "inner join customers c on c.id = o.customerid " +
        "left join details d on d.orderid = o.id;";

    [Fact]
    public void OnCondition_StartsItsOwnLine_PerOnConditionNewLine()
    {
        var result = new FormatterPipeline().Format(MultiJoin, LoadDefaultStyle());
        Assert.True(result.ValidationPassed, result.FormattedText);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).ToArray();

        // Each ON condition on its own (indented) line…
        Assert.Contains(lines, l => l.StartsWith("ON c.id = o.customerid", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.StartsWith("ON d.orderid = o.id", StringComparison.OrdinalIgnoreCase));

        // …and no JOIN line carries its ON inline.
        Assert.DoesNotContain(lines, l =>
            l.Contains("JOIN ", StringComparison.OrdinalIgnoreCase) &&
            l.Contains(" ON ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MergeOn_IsNotAJoinOn_StaysInline()
    {
        // A MERGE's ON is not a join ON-condition: it stays inline and the WHEN clauses must not
        // be pulled up (the regression that kept ON out of the universal boundary set).
        const string merge =
            "merge into dbo.target as t using dbo.source as s on t.id = s.id " +
            "when matched then update set t.value = s.value " +
            "when not matched by target then insert (id, value) values (s.id, s.value);";
        var result = new FormatterPipeline().Format(merge, LoadDefaultStyle());
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).ToArray();

        // ON stays on the USING line (inline), and no ON line carries a WHEN.
        Assert.Contains(lines, l =>
            l.Contains("using", StringComparison.OrdinalIgnoreCase) &&
            l.Contains("ON t.id = s.id", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l =>
            l.Contains("ON t.id", StringComparison.OrdinalIgnoreCase) &&
            l.Contains("WHEN", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OnCondition_Layout_IsIdempotent()
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(MultiJoin, profile);
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
