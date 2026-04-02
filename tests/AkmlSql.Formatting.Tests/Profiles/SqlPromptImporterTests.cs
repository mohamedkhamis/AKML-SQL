using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

public class SqlPromptImporterTests
{
    // ── Basic import ──────────────────────────────────────────────────────

    [Fact]
    public void Import_ValidXml_ReturnsResult()
    {
        const string xml = """
            <SqlPromptStyle>
              <KeywordCasing>UPPERCASE</KeywordCasing>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.NotNull(result);
        Assert.NotNull(result.Profile);
    }

    [Fact]
    public void Import_KeywordCasing_MapsToProfile()
    {
        const string xml = """
            <SqlPromptStyle>
              <KeywordCasing>UPPERCASE</KeywordCasing>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.Equal("UPPERCASE", result.Profile.Casing.ReservedKeywords);
    }

    [Fact]
    public void Import_LowercaseKeywords_MapsToLowercase()
    {
        const string xml = """
            <SqlPromptStyle>
              <KeywordCasing>lowercase</KeywordCasing>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.Equal("lowercase", result.Profile.Casing.ReservedKeywords);
    }

    // ── Option-style XML ──────────────────────────────────────────────────

    [Fact]
    public void Import_OptionStyleXml_MapsCorrectly()
    {
        const string xml = """
            <SqlPromptStyle>
              <Options>
                <Option Name="KeywordCasing" Value="UPPERCASE" />
                <Option Name="TabSize" Value="4" />
              </Options>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.Equal("UPPERCASE", result.Profile.Casing.ReservedKeywords);
        Assert.Equal(4, result.Profile.Whitespace.TabSize);
    }

    // ── MappedCount / UnmappedCount ───────────────────────────────────────

    [Fact]
    public void Import_KnownOption_MappedCountIncremented()
    {
        const string xml = """
            <SqlPromptStyle>
              <KeywordCasing>UPPERCASE</KeywordCasing>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.True(result.MappedCount >= 1);
    }

    [Fact]
    public void Import_UnknownOption_UnmappedCountIncremented()
    {
        const string xml = """
            <SqlPromptStyle>
              <UnknownFancyOption>whatever</UnknownFancyOption>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.True(result.UnmappedCount >= 1);
        Assert.Contains("UnknownFancyOption", result.UnmappedOptions);
    }

    // ── ToBool mapping ────────────────────────────────────────────────────

    [Fact]
    public void Import_InsertTabs_True_SetsTabs()
    {
        const string xml = """
            <SqlPromptStyle>
              <InsertTabs>true</InsertTabs>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.Equal("tabs", result.Profile.Whitespace.TabStyle);
    }

    [Fact]
    public void Import_InsertTabs_False_SetsSpaces()
    {
        const string xml = """
            <SqlPromptStyle>
              <InsertTabs>false</InsertTabs>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.Equal("spaces", result.Profile.Whitespace.TabStyle);
    }

    // ── Profile name ──────────────────────────────────────────────────────

    [Fact]
    public void Import_WithProfileName_SetsMetadataName()
    {
        const string xml = "<SqlPromptStyle />";

        var result = SqlPromptImporter.Import(xml, "My Custom Profile");

        Assert.Equal("My Custom Profile", result.Profile.Metadata.Name);
    }

    [Fact]
    public void Import_NoProfileName_DefaultName()
    {
        const string xml = "<SqlPromptStyle />";

        var result = SqlPromptImporter.Import(xml);

        Assert.False(string.IsNullOrEmpty(result.Profile.Metadata.Name));
    }

    // ── Multiple options ──────────────────────────────────────────────────

    [Fact]
    public void Import_MultipleOptions_AllMapped()
    {
        const string xml = """
            <SqlPromptStyle>
              <KeywordCasing>UPPERCASE</KeywordCasing>
              <TabSize>2</TabSize>
              <AlignAliases>true</AlignAliases>
            </SqlPromptStyle>
            """;

        var result = SqlPromptImporter.Import(xml);

        Assert.Equal("UPPERCASE", result.Profile.Casing.ReservedKeywords);
        Assert.Equal(2, result.Profile.Whitespace.TabSize);
        Assert.True(result.Profile.List.AlignAliases);
        Assert.Equal(3, result.MappedCount);
    }

    // ── Invalid XML ───────────────────────────────────────────────────────

    [Fact]
    public void Import_InvalidXml_NoThrow()
    {
        var ex = Record.Exception(() => SqlPromptImporter.Import("this is not XML"));

        Assert.Null(ex);
    }
}
