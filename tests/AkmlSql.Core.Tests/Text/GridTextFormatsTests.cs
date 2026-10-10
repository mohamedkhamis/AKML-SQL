using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 041 (T043, research R5/R12/R13, FR-030/FR-031) — the results-grid text formats shared by both
/// editions. The Copy As formatters moved here from the SSMS edition's <c>GridCopyAsMenu</c>; the
/// goldens were captured from the SSMS code BEFORE the move (T042), so these tests prove the SSMS
/// edition's output is byte for byte unchanged, quirks included (<c>1,000</c> reads as a number,
/// <c>12.50</c> becomes <c>12.5</c> in JSON, a <c>|</c> is escaped in Markdown cells but not headers).
/// </summary>
public class GridTextFormatsTests
{
    private static readonly JsonElement Goldens = LoadGoldens();

    private static JsonElement LoadGoldens()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "tests", "AkmlSql.Core.Tests", "Text", "GridTextFormats.goldens.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static string[] Headers => Goldens.GetProperty("headers").EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static List<string[]> Rows(string name = "rows") =>
        Goldens.GetProperty(name).EnumerateArray().Select(r => r.EnumerateArray().Select(e => e.GetString()!).ToArray()).ToList();

    private static string Golden(string name) => Goldens.GetProperty(name).GetString()!;

    [Fact] public void Csv_matches_the_SSMS_edition() => Assert.Equal(Golden("csv"), GridTextFormats.Csv(Headers, Rows()));
    [Fact] public void Tsv_matches_the_SSMS_edition() => Assert.Equal(Golden("tsv"), GridTextFormats.Tsv(Headers, Rows()));
    [Fact] public void Json_matches_the_SSMS_edition() => Assert.Equal(Golden("json"), GridTextFormats.Json(Headers, Rows()));
    [Fact] public void Xml_matches_the_SSMS_edition() => Assert.Equal(Golden("xml"), GridTextFormats.Xml(Headers, Rows()));
    [Fact] public void Html_matches_the_SSMS_edition() => Assert.Equal(Golden("html"), GridTextFormats.Html(Headers, Rows()));
    [Fact] public void Insert_matches_the_SSMS_edition() => Assert.Equal(Golden("insert"), GridTextFormats.Insert(Headers, Rows()));
    [Fact] public void Markdown_matches_the_SSMS_edition() => Assert.Equal(Golden("markdown"), GridTextFormats.Markdown(Headers, Rows()));

    [Fact]
    public void InClause_matches_the_SSMS_edition() =>
        Assert.Equal(Golden("inClause"), GridTextFormats.InClause(new[] { "code" }, Rows("inClauseRows")));

    [Fact]
    public void QuoteSqlValue_matches_the_SSMS_edition()
    {
        foreach (var pair in Goldens.GetProperty("quote").EnumerateObject())
        {
            Assert.Equal(pair.Value.GetString(), GridTextFormats.QuoteSqlValue(pair.Name));
        }
    }

    [Fact]
    public void Lines_end_in_CRLF_whatever_the_platform()
    {
        // The browser runtime's AppendLine writes "\n"; the formats must not depend on it.
        Assert.Contains("\r\n", GridTextFormats.Csv(Headers, Rows()));
        Assert.DoesNotContain("\n", GridTextFormats.Csv(Headers, Rows()).Replace("\r\n", string.Empty).Replace("line\r\nbreak", string.Empty));
    }

    [Fact]
    public void Tsv_without_a_header_omits_the_first_line()
    {
        var rows = new List<string[]> { new[] { "1", "a" }, new[] { "NULL", "b" } };

        Assert.Equal("1\ta\r\nNULL\tb\r\n", GridTextFormats.Tsv(new[] { "id", "name" }, rows, includeHeader: false));
        Assert.Equal("id\tname\r\n1\ta\r\nNULL\tb\r\n", GridTextFormats.Tsv(new[] { "id", "name" }, rows));
    }

    [Fact]
    public void Insert_can_name_the_table()
    {
        var rows = new List<string[]> { new[] { "1" } };
        Assert.Equal("INSERT INTO [dbo].[Orders] ([id]) VALUES (1);\r\n", GridTextFormats.Insert(new[] { "id" }, rows, "[dbo].[Orders]"));
        Assert.Equal("INSERT INTO [TableName] ([id]) VALUES (1);\r\n", GridTextFormats.Insert(new[] { "id" }, rows));
    }

    // ── Save results as (FR-030) ──────────────────────────────────────────────────────────────

    [Fact]
    public void Delimited_writes_a_header_row_and_CRLF_lines()
    {
        var text = GridTextFormats.Delimited(new[] { "id", "name" }, new List<string[]> { new[] { "1", "a" } }, ',', retainLineBreaks: true);
        Assert.Equal("id,name\r\n1,a\r\n", text);
    }

    [Theory]
    [InlineData(',', "a,b", "\"a,b\"")]
    [InlineData(',', "say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData(',', "tab\there", "tab\there")]
    [InlineData('\t', "tab\there", "\"tab\there\"")]
    [InlineData('\t', "a,b", "a,b")]
    [InlineData(',', "plain", "plain")]
    public void Delimited_quotes_a_value_holding_the_delimiter_or_a_quote(char delimiter, string value, string expected)
    {
        var text = GridTextFormats.Delimited(new[] { "v" }, new List<string[]> { new[] { value } }, delimiter, retainLineBreaks: true);
        Assert.Equal("v\r\n" + expected + "\r\n", text);
    }

    [Fact]
    public void Delimited_keeps_and_quotes_line_breaks_when_asked()
    {
        var text = GridTextFormats.Delimited(new[] { "v" }, new List<string[]> { new[] { "a\r\nb" } }, ',', retainLineBreaks: true);
        Assert.Equal("v\r\n\"a\r\nb\"\r\n", text);
    }

    [Fact]
    public void Delimited_replaces_line_breaks_with_a_space_by_default_as_SSMS_does()
    {
        var text = GridTextFormats.Delimited(new[] { "v" }, new List<string[]> { new[] { "a\r\nb\nc\rd" } }, ',', retainLineBreaks: false);
        Assert.Equal("v\r\na b c d\r\n", text);
    }

    [Fact]
    public void Delimited_writes_NULL_as_NULL()
    {
        var text = GridTextFormats.Delimited(new[] { "v" }, new List<string[]> { new[] { GridTextFormats.NullText } }, ',', retainLineBreaks: false);
        Assert.Equal("v\r\nNULL\r\n", text);
    }

    [Fact]
    public void Delimited_quotes_headers_too()
    {
        var text = GridTextFormats.Delimited(new[] { "a,b" }, new List<string[]>(), ',', retainLineBreaks: false);
        Assert.Equal("\"a,b\"\r\n", text);
    }
}
