using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.PostgreSql.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.PostgreSql.Tests.Formatting;

/// <summary>
/// Spec 042 (task T145, FR-145). Every option of the SQL Prompt style catalog is either read by the PostgreSQL formatter or listed in
/// <see cref="SqlPromptApplicability"/> as having no effect, and the two halves are kept honest by the formatter itself:
/// <list type="bullet">
///   <item>an option listed as "no effect" never changes the output, whatever its value;</item>
///   <item>an option not listed changes the output of at least one script of the corpus for at least one of its values - an option that
///   never does is either dead (and belongs in the list) or untested (and the corpus needs the script that shows it).</item>
/// </list>
/// </summary>
public class PgStyleApplicabilityTests
{
    private readonly ITestOutputHelper _output;

    public PgStyleApplicabilityTests(ITestOutputHelper output) => _output = output;

    private static string CorpusDirectory => Path.Combine(PgFormatTestStyles.RepoRoot(), "tests", "pg-format-corpus", "corpus");

    private static readonly Lazy<List<(string Name, string Sql)>> Corpus = new(() =>
        Directory.GetFiles(CorpusDirectory, "*.sql").OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileNameWithoutExtension(f), File.ReadAllText(f))).ToList());

    /// <summary>The style's text for each script, or the original when the script was left unchanged.</summary>
    private static List<string> FormatAll(SqlPromptStyleDocument document)
    {
        var style = new SqlPromptStyle(document);
        // The idempotency check is the corpus test's job; here only the layout is compared.
        var options = new PgFormatOptions(CheckIdempotency: false);
        return Corpus.Value.Select(s => PgFormatter.Format(s.Sql, style, options).Text).ToList();
    }

    private static SqlPromptStyleDocument Fresh() => SqlPromptStyleDocument.CreateDefault("probe", "probe");

    /// <summary>The values to try for an option other than its default.</summary>
    private static IEnumerable<string> OtherValues(SqlPromptOption option)
    {
        switch (option.Kind)
        {
            case SqlPromptOptionKind.Boolean:
                yield return option.Default == "true" ? "false" : "true";
                break;
            case SqlPromptOptionKind.Integer:
                var current = int.Parse(option.Default, System.Globalization.CultureInfo.InvariantCulture);
                var min = option.Min ?? 0;
                var max = option.Max ?? 1000;
                foreach (var v in new[] { Math.Max(min, 1), Math.Min(max, 20), Math.Min(max, current + 8), Math.Min(max, 200) }.Distinct())
                    if (v != current) yield return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;
            default:
                foreach (var choice in option.Choices)
                    if (choice.Value != option.Default) yield return choice.Value;
                break;
        }
    }

    /// <summary>
    /// The styles an option is tried against: the defaults; a narrow page, where long contents have to break; and a narrow page that also
    /// puts the first list item on a line of its own and the clauses to the first item. An option waiting for a switch gets it turned on.
    /// </summary>
    private static IEnumerable<SqlPromptStyleDocument> ProbeStyles(SqlPromptOption option)
    {
        foreach (var variant in new[] { 0, 1, 2 })
        {
            var document = Fresh();
            if (variant >= 1)
            {
                document.Set("whitespace.wrapLongLines", "true");
                document.Set("whitespace.wrapLinesLongerThan", "40");
            }
            if (variant == 2)
            {
                document.Set("lists.placeFirstItemOnNewLine", "always");
                document.Set("lists.placeSubsequentItemsOnNewLines", "ifLongerThanMaxLineLength");
                document.Set("insertStatements.values.placeSubsequentValuesOnNewLines", "ifLongerThanMaxLineLength");
            }
            if (option.EnabledWhen is { } gate) document.Set(gate.Path, gate.Value);
            yield return document;
        }
    }

    private static SqlPromptStyleDocument ProbeStyle(SqlPromptOption option) => ProbeStyles(option).First();

    [Fact]
    public void Every_listed_option_exists_in_the_catalog_and_has_a_reason()
    {
        foreach (var (path, reason) in SqlPromptApplicability.NoEffectOptions)
        {
            Assert.NotNull(SqlPromptOptionCatalog.Find(path));
            Assert.False(string.IsNullOrWhiteSpace(reason), path);
        }
    }

    [Fact]
    public void The_catalog_reports_the_marks_and_a_PostgreSQL_sample_for_every_page()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(SqlPromptOptionCatalog.ToEditorSchemaJson());
        var settings = schema.RootElement.GetProperty("settings").EnumerateArray().ToList();
        var marked = settings.Where(s => s.TryGetProperty("noEffectForPostgreSql", out var v) && v.GetBoolean()).Select(s => s.GetProperty("sqlPromptKey").GetString()!).ToList();

        Assert.Equal(SqlPromptApplicability.NoEffectOptions.Keys.OrderBy(k => k, StringComparer.Ordinal), marked.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var group in schema.RootElement.GetProperty("groups").EnumerateArray())
            Assert.False(string.IsNullOrWhiteSpace(group.GetProperty("samplePostgreSql").GetString()), group.GetProperty("id").GetString());
    }

    [Fact]
    public void Every_PostgreSQL_sample_formats_with_every_built_in_style()
    {
        foreach (var section in SqlPromptOptionCatalog.Sections)
        {
            var sample = SqlPromptPreviewSamples.ForPostgreSql(section.Id);
            foreach (var name in PgFormatTestStyles.Names)
            {
                var result = PgFormatter.Format(sample, PgFormatTestStyles.Load(name));
                Assert.True(result.Success, $"{section.Id}/{name}: {string.Join(" | ", result.Diagnostics.Select(d => d.Message))}");
            }
        }
        Assert.True(PgFormatter.Format(SqlPromptPreviewSamples.PostgreSqlGeneral, PgFormatTestStyles.Load("default")).Success);
    }

    [Fact]
    public void An_option_with_no_effect_for_PostgreSQL_never_changes_the_output()
    {
        var baseline = FormatAll(Fresh());
        var failures = new List<string>();

        foreach (var (path, _) in SqlPromptApplicability.NoEffectOptions)
        {
            var option = SqlPromptOptionCatalog.Find(path)!;
            foreach (var value in OtherValues(option))
            {
                var document = ProbeStyle(option);
                document.Set(path, value);
                var changed = FormatAll(document);
                for (var i = 0; i < changed.Count; i++)
                    if (changed[i] != baseline[i]) { failures.Add($"{path} = {value} changed {Corpus.Value[i].Name}"); break; }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Every_other_option_changes_the_output_of_some_script_for_some_value()
    {
        var inert = new List<string>();

        foreach (var option in SqlPromptOptionCatalog.Options)
        {
            if (SqlPromptApplicability.IsNoEffectForPostgreSql(option.Path)) continue;

            var changesSomething = false;
            var variants = ProbeStyles(option).Count();
            for (var variant = 0; variant < variants && !changesSomething; variant++)
            {
                foreach (var value in OtherValues(option))
                {
                    var baselineDocument = ProbeStyles(option).ElementAt(variant);
                    var changedDocument = ProbeStyles(option).ElementAt(variant);
                    changedDocument.Set(option.Path, value);
                    if (!FormatAll(baselineDocument).SequenceEqual(FormatAll(changedDocument))) { changesSomething = true; break; }
                }
            }
            if (!changesSomething) inert.Add(option.Path);
        }

        foreach (var path in inert) _output.WriteLine("inert: " + path);
        Assert.True(inert.Count == 0, "Options that change nothing for any corpus script (list them as no effect, or add the script that shows them):\n" + string.Join("\n", inert));
    }
}
