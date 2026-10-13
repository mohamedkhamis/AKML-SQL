using System.Text;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 041 (T163, research R64, FR-072, FR-089) — the document model, pure: SSMS's default names, a file name made
/// safe, the browser title, and a file read the way SSMS reads one (UTF-8 with or without BOM, UTF-16 with BOM),
/// refused above the 10 MB limit.
/// </summary>
public sealed class EditorDocumentTests
{
    [Theory]
    [InlineData(1, "SQLQuery1")]
    [InlineData(12, "SQLQuery12")]
    [InlineData(0, "SQLQuery1")]
    public void Default_names_are_SQLQuery_n(int n, string expected)
    {
        Assert.Equal(expected, EditorDocument.DefaultName(n));
    }

    [Theory]
    [InlineData("SQLQuery1", true)]
    [InlineData("SQLQuery42", true)]
    [InlineData("SQLQuery", false)]
    [InlineData("sqlquery1", false)]
    [InlineData("SQLQuery1 copy", false)]
    [InlineData("orders-audit", false)]
    [InlineData(null, false)]
    public void A_default_name_is_recognised(string? name, bool expected)
    {
        Assert.Equal(expected, EditorDocument.IsDefaultName(name));
    }

    [Theory]
    [InlineData("orders-audit.sql", "orders-audit")]
    [InlineData("  report:2026?.txt ", "report2026")]
    [InlineData("a/b\\c*d|e\"f<g>h.sql", "abcdefgh")]
    [InlineData("monthly.close.sql", "monthly.close")]
    [InlineData("...", "SQLQuery3")]
    [InlineData("", "SQLQuery3")]
    [InlineData(null, "SQLQuery3")]
    public void A_file_name_is_made_safe_and_loses_its_extension(string? input, string expected)
    {
        Assert.Equal(expected, EditorDocument.NameFromFile(input, nextDefaultNumber: 3));
    }

    [Theory]
    [InlineData("Q3.2025 revenue", "Q3.2025 revenue")]
    [InlineData("report v1.2", "report v1.2")]
    [InlineData("orders-audit.sql", "orders-audit")]
    [InlineData("orders-audit.SQL", "orders-audit")]
    [InlineData("notes.txt", "notes.txt")]
    [InlineData("  a/b:c?.sql ", "abc")]
    [InlineData("...", "SQLQuery3")]
    [InlineData(null, "SQLQuery3")]
    public void A_typed_name_keeps_its_dots_and_loses_only_a_sql_extension(string? input, string expected)
    {
        Assert.Equal(expected, EditorDocument.SanitizeFileName(input, nextDefaultNumber: 3));
    }

    [Fact]
    public void The_title_reads_name_modified_server_database_and_the_product()
    {
        Assert.Equal("orders-audit* - SERVER.db - AKML SQL", EditorDocument.TitleFor("orders-audit", true, "SERVER", "db"));
        Assert.Equal("orders-audit - SERVER.db - AKML SQL", EditorDocument.TitleFor("orders-audit", false, "SERVER", "db"));
        Assert.Equal("SQLQuery1 - AKML SQL", EditorDocument.TitleFor("SQLQuery1", false, null, null));
        Assert.Equal("SQLQuery1* - AKML SQL", EditorDocument.TitleFor("SQLQuery1", true, "", null));
    }

    private const string Sample = "SELECT N'Grüße' AS hello; -- ✓";

    [Fact]
    public void UTF8_with_and_without_a_BOM_reads_the_same()
    {
        var plain = Encoding.UTF8.GetBytes(Sample);
        var bom = new UTF8Encoding(true).GetPreamble().Concat(plain).ToArray();

        Assert.Equal(Sample, EditorDocument.DecodeFile(plain).Text);
        Assert.Equal(Sample, EditorDocument.DecodeFile(bom).Text);
        Assert.Null(EditorDocument.DecodeFile(bom).Error);
    }

    [Fact]
    public void UTF16_little_and_big_endian_with_a_BOM_read()
    {
        var le = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Sample)).ToArray();
        var be = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(Sample)).ToArray();

        Assert.Equal(Sample, EditorDocument.DecodeFile(le).Text);
        Assert.Equal(Sample, EditorDocument.DecodeFile(be).Text);
    }

    [Fact]
    public void A_file_over_the_limit_is_refused_naming_10_MB()
    {
        var big = Encoding.ASCII.GetBytes(new string('x', DocumentSizeLimit.MaxDocumentSizeChars + 1));

        var result = EditorDocument.DecodeFile(big);

        Assert.Null(result.Text);
        Assert.Contains("10 MB", result.Error);
    }

    [Fact]
    public void The_limit_also_bounds_the_UTF8_size_of_the_text()
    {
        // Under the character limit, but more than 10 MB once sent as UTF-8 (the engine frames are UTF-8).
        var text = new string('€', DocumentSizeLimit.MaxDocumentSizeChars / 2);
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();

        var result = EditorDocument.DecodeFile(utf16);

        Assert.Null(result.Text);
        Assert.Contains("10 MB", result.Error);
    }

    [Fact]
    public void A_file_without_a_BOM_that_is_not_UTF8_reads_as_Windows_1252()
    {
        // SSMS's ANSI save: Caf\xE9, \xA3 and the 0x80\u20139F range (\x80 = \u20AC, \x93 \x94 = curly quotes).
        var ansi = new byte[] { 0x43, 0x61, 0x66, 0xE9, 0x20, 0xA3, 0x35, 0x20, 0x80, 0x20, 0x93, 0x68, 0x69, 0x94 };

        var result = EditorDocument.DecodeFile(ansi);

        Assert.Null(result.Error);
        Assert.Equal("Caf\u00e9 \u00a35 \u20ac \u201chi\u201d", result.Text);
        Assert.DoesNotContain('\uFFFD', result.Text);
    }

    [Fact]
    public void Valid_UTF8_without_a_BOM_is_still_read_as_UTF8()
    {
        var text = "SELECT N'Caf\u00e9 \u00a3 \u20ac';";

        Assert.Equal(text, EditorDocument.DecodeFile(Encoding.UTF8.GetBytes(text)).Text);
    }

    [Fact]
    public void An_empty_file_is_an_empty_document()
    {
        var result = EditorDocument.DecodeFile(Array.Empty<byte>());

        Assert.Equal(string.Empty, result.Text);
        Assert.Null(result.Error);
    }
}
