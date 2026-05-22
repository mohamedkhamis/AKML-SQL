using System.Xml.Linq;
using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

public class SqlPromptExporterTests
{
    // ── Basic export ──────────────────────────────────────────────────────

    [Fact]
    public void Export_DefaultProfile_ReturnsValidXml()
    {
        var profile = new FormattingProfile();

        var result = SqlPromptExporter.Export(profile);

        Assert.NotNull(result);
        Assert.NotNull(result.Xml);
        Assert.NotEqual(string.Empty, result.Xml);
        Assert.True(result.WrittenCount > 0, "Exporter wrote 0 options — expected at least one default to be emitted");
    }

    [Fact]
    public void Export_OutputParsesAsValidXml()
    {
        var profile = new FormattingProfile();
        var result = SqlPromptExporter.Export(profile);

        var doc = XDocument.Parse(result.Xml);

        Assert.Equal("SqlPromptStyle", doc.Root?.Name.LocalName);
        Assert.NotNull(doc.Root?.Element("Options"));
    }

    [Fact]
    public void Export_OutputUsesOptionNameValueShape()
    {
        var profile = new FormattingProfile { Casing = { ReservedKeywords = "UPPERCASE" } };
        var result = SqlPromptExporter.Export(profile);

        var doc = XDocument.Parse(result.Xml);
        var option = doc.Root?.Element("Options")?
            .Elements("Option")
            .FirstOrDefault(e => string.Equals(e.Attribute("Name")?.Value, "KeywordCasing", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(option);
        Assert.Equal("UPPERCASE", option!.Attribute("Value")?.Value);
    }

    // ── Specific setting round-trips ──────────────────────────────────────

    [Fact]
    public void Export_TabSize_EmittedCorrectly()
    {
        var profile = new FormattingProfile { Whitespace = { TabSize = 2 } };

        var doc = XDocument.Parse(SqlPromptExporter.Export(profile).Xml);
        var opt = doc.Descendants("Option")
            .FirstOrDefault(e => string.Equals(e.Attribute("Name")?.Value, "TabSize", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(opt);
        Assert.Equal("2", opt!.Attribute("Value")?.Value);
    }

    [Fact]
    public void Export_InsertTabs_TabsStyle_EmitsTrue()
    {
        var profile = new FormattingProfile { Whitespace = { TabStyle = "tabs" } };

        var doc = XDocument.Parse(SqlPromptExporter.Export(profile).Xml);
        var opt = doc.Descendants("Option")
            .FirstOrDefault(e => string.Equals(e.Attribute("Name")?.Value, "InsertTabs", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("true", opt?.Attribute("Value")?.Value);
    }

    [Fact]
    public void Export_InsertTabs_SpacesStyle_EmitsFalse()
    {
        var profile = new FormattingProfile { Whitespace = { TabStyle = "spaces" } };

        var doc = XDocument.Parse(SqlPromptExporter.Export(profile).Xml);
        var opt = doc.Descendants("Option")
            .FirstOrDefault(e => string.Equals(e.Attribute("Name")?.Value, "InsertTabs", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("false", opt?.Attribute("Value")?.Value);
    }

    [Fact]
    public void Export_CommaPosition_Leading_EmitsBefore()
    {
        var profile = new FormattingProfile { List = { CommaPosition = "leading" } };

        var doc = XDocument.Parse(SqlPromptExporter.Export(profile).Xml);
        var opt = doc.Descendants("Option")
            .FirstOrDefault(e => string.Equals(e.Attribute("Name")?.Value, "CommaPosition", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("before", opt?.Attribute("Value")?.Value);
    }

    [Fact]
    public void Export_CommaPosition_Trailing_EmitsAfter()
    {
        var profile = new FormattingProfile { List = { CommaPosition = "trailing" } };

        var doc = XDocument.Parse(SqlPromptExporter.Export(profile).Xml);
        var opt = doc.Descendants("Option")
            .FirstOrDefault(e => string.Equals(e.Attribute("Name")?.Value, "CommaPosition", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("after", opt?.Attribute("Value")?.Value);
    }

    // ── Round-trip: Import → Export produces equivalent XML ───────────────

    [Fact]
    public void RoundTrip_ImportThenExport_PreservesKeyCasingValues()
    {
        const string sourceXml = """
            <SqlPromptStyle>
              <Options>
                <Option Name="KeywordCasing" Value="UPPERCASE" />
                <Option Name="FunctionCasing" Value="UPPERCASE" />
                <Option Name="DataTypeCasing" Value="lowercase" />
                <Option Name="TabSize" Value="4" />
                <Option Name="AlignAliases" Value="true" />
              </Options>
            </SqlPromptStyle>
            """;

        var imported = SqlPromptImporter.Import(sourceXml);
        var exported = SqlPromptExporter.Export(imported.Profile);

        var doc = XDocument.Parse(exported.Xml);
        var options = doc.Descendants("Option").ToDictionary(
            e => e.Attribute("Name")!.Value,
            e => e.Attribute("Value")!.Value,
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal("UPPERCASE", options["KeywordCasing"]);
        Assert.Equal("UPPERCASE", options["FunctionCasing"]);
        Assert.Equal("lowercase", options["DataTypeCasing"]);
        Assert.Equal("4",         options["TabSize"]);
        Assert.Equal("true",      options["AlignAliases"]);
    }

    [Fact]
    public void RoundTrip_ImportExportImport_ProducesIdenticalSettings()
    {
        const string sourceXml = """
            <SqlPromptStyle>
              <Options>
                <Option Name="KeywordCasing" Value="lowercase" />
                <Option Name="CommaPosition" Value="before" />
                <Option Name="TabSize" Value="2" />
                <Option Name="WhereOnNewLine" Value="true" />
                <Option Name="JoinOnNewLine" Value="true" />
              </Options>
            </SqlPromptStyle>
            """;

        var first = SqlPromptImporter.Import(sourceXml);
        var firstXml = SqlPromptExporter.Export(first.Profile).Xml;
        var second = SqlPromptImporter.Import(firstXml);

        // Compare the SECOND-round profile against the first-round profile on the keys we set
        Assert.Equal(first.Profile.Casing.ReservedKeywords, second.Profile.Casing.ReservedKeywords);
        Assert.Equal(first.Profile.List.CommaPosition,      second.Profile.List.CommaPosition);
        Assert.Equal(first.Profile.Whitespace.TabSize,      second.Profile.Whitespace.TabSize);
        Assert.Equal(first.Profile.Dml.WhereOnNewLine,      second.Profile.Dml.WhereOnNewLine);
        Assert.Equal(first.Profile.Join.OnNewLine,          second.Profile.Join.OnNewLine);
    }

    // ── Parenthesis collapse (T076) ───────────────────────────────────────

    [Fact]
    public void Export_ParenthesisCollapseSettings_EmittedCorrectly()
    {
        var profile = new FormattingProfile { Parenthesis = { CollapseShort = true, CollapseThreshold = 72 } };

        var doc = XDocument.Parse(SqlPromptExporter.Export(profile).Xml);
        var options = doc.Descendants("Option").ToDictionary(
            e => e.Attribute("Name")!.Value,
            e => e.Attribute("Value")!.Value,
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal("true", options["CollapseShortParenthesisContents"]);
        Assert.Equal("72",   options["CollapseParenthesesShorterThan"]);
    }

    [Fact]
    public void RoundTrip_ParenthesisCollapse_PreservesSettings()
    {
        var profile = new FormattingProfile { Parenthesis = { CollapseShort = false, CollapseThreshold = 95 } };

        var xml = SqlPromptExporter.Export(profile).Xml;
        var reimported = SqlPromptImporter.Import(xml);

        Assert.False(reimported.Profile.Parenthesis.CollapseShort);
        Assert.Equal(95, reimported.Profile.Parenthesis.CollapseThreshold);
    }

    // ── DML collapse (T077) ───────────────────────────────────────────────

    [Fact]
    public void Export_DmlCollapseSettings_EmittedCorrectly()
    {
        var profile = new FormattingProfile
        {
            Dml = { CollapseShortStatements = true, CollapseThreshold = 90,
                    CollapseShortSubqueries = true, SubqueryCollapseThreshold = 70 }
        };

        var doc = XDocument.Parse(SqlPromptExporter.Export(profile).Xml);
        var options = doc.Descendants("Option").ToDictionary(
            e => e.Attribute("Name")!.Value,
            e => e.Attribute("Value")!.Value,
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal("true", options["DmlCollapseShortStatements"]);
        Assert.Equal("90",   options["DmlCollapseStatementsShorterThan"]);
        Assert.Equal("true", options["DmlCollapseShortSubqueries"]);
        Assert.Equal("70",   options["DmlCollapseSubqueriesShorterThan"]);
    }

    [Fact]
    public void RoundTrip_DmlCollapse_PreservesSettings()
    {
        var profile = new FormattingProfile
        {
            Dml = { CollapseShortStatements = false, CollapseThreshold = 110,
                    CollapseShortSubqueries = false, SubqueryCollapseThreshold = 130 }
        };

        var xml = SqlPromptExporter.Export(profile).Xml;
        var reimported = SqlPromptImporter.Import(xml);

        Assert.False(reimported.Profile.Dml.CollapseShortStatements);
        Assert.Equal(110, reimported.Profile.Dml.CollapseThreshold);
        Assert.False(reimported.Profile.Dml.CollapseShortSubqueries);
        Assert.Equal(130, reimported.Profile.Dml.SubqueryCollapseThreshold);
    }

    // ── Coverage / metadata ───────────────────────────────────────────────

    [Fact]
    public void Export_KnownOptionCount_NonZero()
    {
        Assert.True(SqlPromptExporter.KnownOptionCount > 0,
            "Exporter ReverseMap should know at least one SQL Prompt option name");
    }

    [Fact]
    public void Export_NullProfile_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SqlPromptExporter.Export(null!));
    }

    // ── File I/O ──────────────────────────────────────────────────────────

    [Fact]
    public void ExportToFile_WritesXmlAtomically()
    {
        var profile = new FormattingProfile { Casing = { ReservedKeywords = "UPPERCASE" } };

        var tempDir = Path.Combine(Path.GetTempPath(), "akml-export-test-" + Guid.NewGuid().ToString("N"));
        var destPath = Path.Combine(tempDir, "out.sqlpromptstylev2");

        try
        {
            var result = SqlPromptExporter.ExportToFile(profile, destPath);

            Assert.True(File.Exists(destPath), "Output file was not created");
            Assert.False(File.Exists(destPath + ".tmp"), "Temp file was not cleaned up after atomic move");
            Assert.NotNull(result);
            Assert.True(result.WrittenCount > 0);

            var roundTrip = SqlPromptImporter.Import(File.ReadAllText(destPath));
            Assert.Equal("UPPERCASE", roundTrip.Profile.Casing.ReservedKeywords);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
