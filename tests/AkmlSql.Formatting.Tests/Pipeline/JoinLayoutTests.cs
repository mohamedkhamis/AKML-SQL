using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T010 — base-pipeline JOIN layout. The join-type modifier (INNER/LEFT/…) and JOIN used
/// to split across two lines (<c>… INNER</c> ⏎ <c>JOIN …</c>): the LineBreakDecider broke before
/// BOTH, and <c>ListRules.ApplyCollapseShortLists</c> swept the trailing modifier into the
/// preceding FROM/JOIN "list" (because the modifier was not a list boundary), pulling it up — and,
/// once the base layout was correct, collapsing the whole FROM+JOIN region onto one line. The fix
/// keeps the modifier+JOIN together on their own line. These lock it through the full pipeline.
/// </summary>
public class JoinLayoutTests
{
    private const string MultiJoin =
        "select o.id, c.name from orders o " +
        "inner join customers c on c.id = o.customerid " +
        "left join details d on d.orderid = o.id;";

    [Fact]
    public void JoinModifier_StaysWithJoin_OnOwnLine()
    {
        var result = new FormatterPipeline().Format(MultiJoin, LoadDefaultStyle());
        Assert.True(result.ValidationPassed, result.FormattedText);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).ToArray();

        // "INNER JOIN" and "LEFT JOIN" each begin their own line (modifier attached to JOIN).
        Assert.Contains(lines, l => l.StartsWith("INNER JOIN ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.StartsWith("LEFT JOIN ", StringComparison.OrdinalIgnoreCase));

        // The split form must not occur: no line ends with a bare join modifier, and no line
        // begins with a bare JOIN (i.e. JOIN never starts a line without its modifier).
        Assert.DoesNotContain(lines, l =>
            l.EndsWith(" INNER", StringComparison.OrdinalIgnoreCase) ||
            l.EndsWith(" LEFT", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("INNER", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("LEFT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.StartsWith("JOIN ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JoinLayout_DoesNotCollapseOntoOneLine()
    {
        var result = new FormatterPipeline().Format(MultiJoin, LoadDefaultStyle());
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');
        // FROM + the two joins must occupy distinct lines, not be merged into one.
        Assert.True(lines.Count(l => l.ToUpperInvariant().Contains("JOIN")) >= 2,
            $"joins collapsed onto one line:\n{result.FormattedText}");
    }

    [Fact]
    public void JoinLayout_IsIdempotent()
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
