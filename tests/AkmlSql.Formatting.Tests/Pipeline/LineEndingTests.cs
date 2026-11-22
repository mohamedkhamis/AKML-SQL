using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 047 (SSMS-004, data-model §12, contracts/formatter.md §4): the formatter writes "\n", and SSMS replaced the whole
/// buffer with it, so Format Document turned a CRLF file into LF. The pipeline now gives the output the input's dominant
/// line ending, after validation, so every host (SSMS, Bulk Format, the CLI) keeps a file's convention.
/// </summary>
public sealed class LineEndingTests
{
    private static FormatResult Format(string sql) => new FormatterPipeline().Format(sql, new FormattingProfile());

    [Fact]
    public void A_CRLF_script_comes_back_CRLF_with_no_lone_LF()
    {
        var result = Format("select a,b from dbo.t where a=1\r\nselect 2\r\n");

        Assert.True(result.Success);
        Assert.Contains("\r\n", result.FormattedText);
        Assert.DoesNotMatch(@"(?<!\r)\n", result.FormattedText);
    }

    [Fact]
    public void An_LF_script_comes_back_LF()
    {
        var result = Format("select a,b from dbo.t where a=1\nselect 2\n");

        Assert.True(result.Success);
        Assert.Contains("\n", result.FormattedText);
        Assert.DoesNotContain("\r", result.FormattedText);
    }

    [Fact]
    public void A_mostly_LF_script_comes_back_LF()
    {
        var result = Format("select 1\r\nselect 2\nselect 3\nselect 4\n");

        Assert.DoesNotContain("\r", result.FormattedText);
    }

    [Fact]
    public void A_one_line_script_has_no_line_ending_to_keep()
    {
        var result = Format("select 1");

        Assert.DoesNotContain("\r", result.FormattedText);
    }

    [Fact]
    public void A_script_that_does_not_parse_comes_back_byte_for_byte()
    {
        const string broken = "selec * frm\r\n  where (\r\n";
        var result = Format(broken);

        Assert.Equal(broken, result.FormattedText);
    }
}
