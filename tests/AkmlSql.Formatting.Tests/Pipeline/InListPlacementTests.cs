using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T013 — <c>inStatements.placeItemsOnNewLine</c> is the canonical SQL Prompt control for
/// IN-list expansion; it was a dead option (round-tripped via the importer/exporter but no layout
/// pass consumed it — spec-020 tagged it GapToImplement). Now wired into <c>ApplyInListStyle</c>
/// with precedence: an explicit <c>always</c>/<c>never</c> drives; the default
/// <c>ifLongerThanWrap</c> defers to the older <c>expression.inListStyle</c> (a string property
/// can't tell "absent" from "explicitly default", so default = defer is the only clean rule — and
/// it keeps AKML's own styles, which leave it default, on their existing inListStyle behavior).
/// Round-trip is preserved (the field is never removed — FR-024).
/// </summary>
public class InListPlacementTests
{
    private const string ShortInList =
        "select * from orders where status in ('A', 'B', 'C') order by orderid;";

    [Fact]
    public void PlaceItemsOnNewLine_Never_KeepsInListInline()
    {
        var profile = LoadDefaultStyle();      // inListStyle defaults to "multiLine" → would expand
        profile.InStatements.PlaceItemsOnNewLine = "never";
        var result = new FormatterPipeline().Format(ShortInList, profile);
        Assert.True(result.ValidationPassed, result.FormattedText);

        // The whole IN list sits on one line — explicit "never" overrides the style's multiLine.
        Assert.Matches(@"IN\s*\(\s*'A',\s*'B',\s*'C'\s*\)",
            result.FormattedText.Replace("\r\n", " ").Replace("\n", " "));
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');
        Assert.DoesNotContain(lines, l => l.Trim() == "'B',");
    }

    [Fact]
    public void PlaceItemsOnNewLine_Always_ExpandsInList()
    {
        var profile = LoadDefaultStyle();
        profile.Expression.InListStyle = "singleLine";   // older control says inline…
        profile.InStatements.PlaceItemsOnNewLine = "always";  // …canonical control overrides → expand
        var result = new FormatterPipeline().Format(ShortInList, profile);
        Assert.True(result.ValidationPassed, result.FormattedText);

        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToArray();
        Assert.Contains(lines, l => l.StartsWith("'B'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlaceItemsOnNewLine_Default_DefersToInListStyle()
    {
        // Default ifLongerThanWrap + inListStyle multiLine (the built-in-style situation) → expanded,
        // exactly as before the wiring — so 07/08 goldens are unaffected.
        var profile = LoadDefaultStyle();
        Assert.Equal("ifLongerThanWrap", profile.InStatements.PlaceItemsOnNewLine);
        var result = new FormatterPipeline().Format(ShortInList, profile);
        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToArray();
        Assert.Contains(lines, l => l.StartsWith("'B'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlaceItemsOnNewLine_IsIdempotent()
    {
        foreach (var mode in new[] { "never", "always" })
        {
            var profile = LoadDefaultStyle();
            profile.InStatements.PlaceItemsOnNewLine = mode;
            var once = new FormatterPipeline().Format(ShortInList, profile);
            var twice = new FormatterPipeline().Format(once.FormattedText, profile);
            Assert.Equal(once.FormattedText, twice.FormattedText);
        }
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
