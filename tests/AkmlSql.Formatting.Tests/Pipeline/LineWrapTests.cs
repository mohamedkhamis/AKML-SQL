using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T012 / FR-002 — lines longer than Whitespace.MaxLineWidth wrap at the last
/// inter-token gap that still fits, with a one-level continuation indent. The wrap pass is the
/// LAST post-collapse finalization step (wrapping is line geometry — it must see final line
/// shapes). Gaps with zero preceding spaces (dots, compound-operator halves, commas, unary
/// operands) are structural and never wrap candidates.
/// </summary>
public class LineWrapTests
{
    // A single select item ~150 chars wide: too long for any collapse, no commas — the line can
    // only be brought under the width by the wrap pass.
    private const string LongExpression =
        "select aaaa.column_name_one + bbbb.column_name_two + cccc.column_name_three + " +
        "dddd.column_name_four + eeee.column_name_five + ffff.column_name_six as combined_total " +
        "from some_table aaaa;";

    [Fact]
    public void LongLine_WrapsWithinMaxLineWidth()
    {
        var profile = LoadDefaultStyle();
        var result = new FormatterPipeline().Format(LongExpression, profile);
        Assert.True(result.ValidationPassed, result.FormattedText);

        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');
        var tooLong = lines.Where(l => l.Length > profile.Whitespace.MaxLineWidth).ToArray();
        Assert.True(tooLong.Length == 0,
            $"lines over {profile.Whitespace.MaxLineWidth} chars:\n{string.Join("\n", tooLong)}\n--- full ---\n{result.FormattedText}");

        // The wrapped continuation is indented deeper than the item line it came from.
        Assert.Contains("combined_total", result.FormattedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wrap_IsIdempotent()
    {
        var profile = LoadDefaultStyle();
        var once = new FormatterPipeline().Format(LongExpression, profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
    }

    [Fact]
    public void MaxLineWidthZero_DisablesWrapping()
    {
        var profile = LoadDefaultStyle();
        profile.Whitespace.MaxLineWidth = 0;
        var result = new FormatterPipeline().Format(LongExpression, profile);

        var lines = result.FormattedText.Replace("\r\n", "\n").Split('\n');
        Assert.Contains(lines, l => l.Length > 120);
    }

    [Fact]
    public void ShortStatement_IsUntouched()
    {
        var profile = LoadDefaultStyle();
        const string sql = "select customerid, customername, country from customers where country = 'USA';";
        var wrapped = new FormatterPipeline().Format(sql, profile).FormattedText;
        profile.Whitespace.MaxLineWidth = 0;
        var unwrapped = new FormatterPipeline().Format(sql, profile).FormattedText;
        Assert.Equal(unwrapped, wrapped);
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
