using AkmlSql.Formatting.SqlPrompt;
using Xunit;

namespace AkmlSql.Formatting.Tests.SqlPrompt;

/// <summary>
/// What the user sees in the style editor: on each page, changing an option must change that
/// page's preview. An option whose effect depends on other settings or on code the sample cannot
/// contain must say so in its note (shown next to the option), instead of silently doing nothing.
/// </summary>
public class SqlPromptPreviewSampleTests
{
    public static TheoryData<string> AllOptions()
    {
        var data = new TheoryData<string>();
        foreach (var o in SqlPromptOptionCatalog.Options) data.Add(o.Path);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllOptions))]
    public void Changing_the_option_changes_its_pages_preview_or_the_option_explains_when_it_applies(string path)
    {
        var option = SqlPromptOptionCatalog.Find(path)!;
        var sql = SqlPromptPreviewSamples.For(option.Section);
        var basis = SqlPromptStyleDocument.CreateDefault("page", "page");
        if (option.EnabledWhen is { } gate) basis.Set(gate.Path, gate.Value);
        var baseline = SqlPromptOptionSensitivityTests.Format(sql, basis);

        var values = option.Kind switch
        {
            SqlPromptOptionKind.Boolean => [option.Default == "true" ? "false" : "true"],
            SqlPromptOptionKind.Integer => ["2", "20", "200"],
            _ => option.Choices.Select(c => c.Value).Where(v => v != option.Default).ToArray(),
        };
        var unchanged = values.Where(v =>
        {
            var doc = basis.Clone();
            doc.Set(path, v);
            return SqlPromptOptionSensitivityTests.Format(sql, doc) == baseline;
        }).ToList();

        var invisible = option.Kind == SqlPromptOptionKind.Integer ? unchanged.Count == values.Length : unchanged.Count > 0;
        if (invisible)
            Assert.False(string.IsNullOrWhiteSpace(option.Note),
                $"{path}: [{string.Join(", ", unchanged)}] do not change the {option.Section} page preview, and the option has no note saying when it applies.");
    }

    [Fact]
    public void Every_page_has_its_own_sample()
    {
        foreach (var section in SqlPromptOptionCatalog.Sections)
            Assert.NotSame(SqlPromptPreviewSamples.General, SqlPromptPreviewSamples.For(section.Id));
    }
}
