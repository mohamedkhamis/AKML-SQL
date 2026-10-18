using System;
using System.IO;
using System.Linq;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// UI test 2026-10-18: Format broke scripts with batches. The first statement of a batch after GO was never registered as a
/// statement start, so one with no line rule of its own (PRINT, EXEC, DECLARE …) stayed on the GO line ("GO PRINT 'x';"),
/// which is no longer a batch separator; and the alias alignment measured every SELECT list of the script together, so a
/// short list was padded out to another statement's column.
/// </summary>
public class BatchSeparatorLayoutTests
{
    private static string Format(string sql)
    {
        var result = new FormatterPipeline().Format(sql, LoadDefaultStyle());
        Assert.True(result.ValidationPassed, result.FormattedText);
        return result.FormattedText.Replace("\r\n", "\n");
    }

    [Theory]
    [InlineData("PRINT 'after';", "PRINT 'after';")]
    [InlineData("EXEC sp_who;", "EXEC sp_who;")]
    [InlineData("DECLARE @i int = 1;", "DECLARE @i int = 1;")]
    public void The_statement_after_GO_starts_its_own_line(string statement, string expected)
    {
        var lines = Format("SELECT 1 AS a;\nGO\n" + statement + "\n").Split('\n');

        Assert.Contains("GO", lines);
        Assert.Contains(lines, l => l.StartsWith(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_GO_with_a_repeat_count_keeps_the_count_and_the_next_statement_starts_its_own_line()
    {
        var lines = Format("SELECT 1 AS a;\nGO 3\nPRINT 'x';\n").Split('\n');

        Assert.Contains(lines, l => l.Trim() == "GO 3");
        Assert.Contains(lines, l => l.StartsWith("PRINT 'x';", StringComparison.Ordinal));
    }

    [Fact]
    public void Aliases_align_within_a_statement_and_never_across_statements_or_batches()
    {
        var text = Format(
            "SELECT a AS x;\nGO\nSELECT bbbbbbbbbbbbbbbbbbbbbbbbbbbb AS y;\nSELECT c AS z;\n");

        Assert.Contains("SELECT a AS x;", text);
        Assert.Contains("SELECT c AS z;", text);
        Assert.Contains("bbbbbbbbbbbbbbbbbbbbbbbbbbbb AS y;", text);
    }

    [Fact]
    public void Two_aliases_on_one_line_are_not_padded_against_each_other()
    {
        var text = Format("SELECT bbbbbbbbbbbbbbbbbbbbbbbbbbbb AS y, c AS z FROM t;\n");

        Assert.DoesNotContain("  AS", text);
    }

    [Fact]
    public void A_multi_line_list_is_still_aligned()
    {
        var text = Format(
            "select c.customername, count(o.orderid) as order_count, sum(o.total) as total_spent " +
            "from customers c group by c.customername;\nGO\nPRINT 'done';\n");
        var asColumns = text.Split('\n')
            .Where(l => l.Contains(" AS ", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToArray();

        Assert.Single(asColumns);
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
