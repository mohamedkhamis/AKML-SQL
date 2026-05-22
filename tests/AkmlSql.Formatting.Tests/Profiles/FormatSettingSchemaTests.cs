using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Spec 020 — PR #239 review follow-up. Guards that <see cref="FormatSettingSchema"/>
/// reports an accurate Implemented / AkmlOnly status for the SQL-Prompt-round-trippable
/// settings — including those whose SQL Prompt importer key deliberately differs from the
/// AKML property name (the per-category "collapseThreshold" settings in particular, which
/// a property-name heuristic alone cannot disambiguate).
/// </summary>
public class FormatSettingSchemaTests
{
    [Theory]
    [InlineData("whitespace.preserveEmptyLinesAfterBatch")]
    [InlineData("list.alignItemsAcrossClauses")]
    [InlineData("parenthesis.collapseShort")]
    [InlineData("parenthesis.collapseThreshold")]
    [InlineData("dml.collapseShortStatements")]
    [InlineData("dml.collapseThreshold")]
    [InlineData("dml.subqueryCollapseThreshold")]
    [InlineData("ddl.firstParameterOnNewLine")]
    [InlineData("ddl.collapseThreshold")]
    [InlineData("controlFlow.collapseThreshold")]
    [InlineData("join.alignJoinKeyword")]
    public void RoundTrippableSetting_StatusIsImplemented(string settingId)
    {
        var schema = FormatSettingSchema.BuildDefault();

        var setting = schema.Settings.Find(s => s.Id == settingId);

        Assert.NotNull(setting);
        Assert.Equal("Implemented", setting!.Status);
        Assert.False(string.IsNullOrEmpty(setting.SqlPromptKey));
    }

    [Fact]
    public void Schema_BuildsWithGroupsAndSettings()
    {
        var schema = FormatSettingSchema.BuildDefault();

        Assert.NotEmpty(schema.Groups);
        Assert.NotEmpty(schema.Settings);
    }
}
