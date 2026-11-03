using AkmlSql.PostgreSql.Formatting;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Formatting;

/// <summary>
/// UI scan 2026-11-03 (W6): with the Khamis Style ("Empty lines between statements: 2") the PostgreSQL formatter put
/// four empty lines between two statements where the T-SQL formatter puts two for the same style. The count must be
/// the style's, whatever the statements are.
/// </summary>
public sealed class PgStatementSpacingTests
{
    [Theory]
    [InlineData("khamis-style", 2)]
    [InlineData("default", 1)]
    [InlineData("compact", 0)]
    public void Empty_lines_between_statements_are_the_styles(string styleName, int expected)
    {
        var style = PgFormatTestStyles.Load(styleName);
        var result = PgFormatter.Format("select 1;\nselect 2;", style);

        Assert.Equal(expected, EmptyLinesBetweenStatements(result.Text));
    }

    [Fact]
    public void A_where_clause_does_not_change_the_spacing()
    {
        var style = PgFormatTestStyles.Load("khamis-style");
        var sql = "select * from akml_uitest_nov03 n where n.flag = true and (n.amount > 1) and n.\"MixedCase\" = 'A';\n"
                + "select n.amount::text, now()::date from akml_uitest_nov03 n;";
        var result = PgFormatter.Format(sql, style);

        Assert.Equal(2, EmptyLinesBetweenStatements(result.Text));
    }

    /// <summary>The empty lines between the first statement's last line and the second statement's SELECT.</summary>
    private static int EmptyLinesBetweenStatements(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var selects = lines.Select((l, i) => (l, i)).Where(x => x.l.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToList();
        Assert.True(selects.Count >= 2, "two SELECT statements expected in:\n" + text);
        var second = selects[1];
        var empty = 0;
        for (var i = second - 1; i > selects[0] && lines[i].Trim().Length == 0; i--) empty++;
        return empty;
    }
}
