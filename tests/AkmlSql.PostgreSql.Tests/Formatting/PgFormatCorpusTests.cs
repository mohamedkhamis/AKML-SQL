using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.PostgreSql.Formatting;
using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Formatting;

/// <summary>
/// Spec 042 (task T144, SC-015, FR-140 to FR-143) - the PostgreSQL format corpus: every script under <c>tests/pg-format-corpus/corpus</c>
/// formatted with every built-in style. For each pair the output is accepted (the script is valid PostgreSQL, so the formatter must
/// format it), means the same statements, formats to itself, keeps every quoted name, string and dollar-quoted body as it was, and
/// equals the golden file byte for byte (after trailing white space and line ends are normalised, as for the T-SQL parity corpus).
///
/// <para>Set <c>AKML_UPDATE_PG_GOLDENS=1</c> to write the goldens; a missing golden is written the same way. A golden is a drift
/// guard, not an authority: review the diff against the style definitions before keeping it.</para>
/// </summary>
[Trait("Category", "FormatCorpus")]
public class PgFormatCorpusTests
{
    private const string UpdateVariable = "AKML_UPDATE_PG_GOLDENS";

    private static readonly Lazy<Dictionary<string, SqlPromptStyle>> Styles = new(() =>
        PgFormatTestStyles.Names.ToDictionary(n => n, PgFormatTestStyles.Load));

    private static string CorpusDirectory => Path.Combine(PgFormatTestStyles.RepoRoot(), "tests", "pg-format-corpus", "corpus");

    private static string GoldenDirectory => Path.Combine(PgFormatTestStyles.RepoRoot(), "tests", "pg-format-corpus", "golden");

    public static IEnumerable<object[]> Matrix()
    {
        foreach (var file in Directory.GetFiles(CorpusDirectory, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
            foreach (var style in PgFormatTestStyles.Names)
                yield return new object[] { Path.GetFileNameWithoutExtension(file), style };
    }

    [Fact]
    public void The_corpus_holds_at_least_a_hundred_scripts()
    {
        Assert.True(Directory.GetFiles(CorpusDirectory, "*.sql").Length >= 100);
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void A_script_formats_safely_and_matches_its_golden(string script, string styleName)
    {
        var input = File.ReadAllText(Path.Combine(CorpusDirectory, script + ".sql"));
        var style = Styles.Value[styleName];

        var result = PgFormatter.Format(input, style);
        Assert.True(result.Success, Describe(script, styleName, input, style, result));
        var output = result.Text;

        // 1. the same statements
        var before = PgParser.Parse(input);
        var after = PgParser.Parse(output);
        Assert.True(after.Success, $"{script}/{styleName}: the output does not parse: {after.Error?.Message}\n{output}");
        Assert.True(PgParser.TreesEquivalent(before.Tree!, after.Tree!), $"{script}/{styleName}: the output means something else:\n{output}");

        // 2. stable
        var again = PgFormatter.Format(output, style);
        Assert.True(again.Success && again.Text == output, $"{script}/{styleName}: formatting the output changes it:\n--- first ---\n{output}\n--- second ---\n{again.Text}");

        // 3. quoted names, strings and dollar-quoted bodies untouched, in order
        Assert.Equal(Literals(input), Literals(output));

        // 4. the golden
        var actual = Normalise(output);
        var goldenPath = Path.Combine(GoldenDirectory, $"{script}__{styleName}.sql");
        var update = string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "1", StringComparison.Ordinal);
        if (update || !File.Exists(goldenPath))
        {
            Directory.CreateDirectory(GoldenDirectory);
            File.WriteAllText(goldenPath, actual);
            Assert.False(string.IsNullOrWhiteSpace(actual), $"{script}/{styleName}: refusing to write an empty golden.");
            return;
        }
        Assert.Equal(Normalise(File.ReadAllText(goldenPath)), actual);
    }

    private static string Describe(string script, string styleName, string input, SqlPromptStyle style, PgFormatResult result) =>
        $"{script}/{styleName} was not formatted: {string.Join(" | ", result.Diagnostics.Select(d => d.Message))}\n--- layout ---\n{PgFormatter.LayoutForTests(input, style)}";

    /// <summary>Quoted identifiers, strings and dollar bodies of a script, in order, as written.</summary>
    private static List<string> Literals(string text) =>
        PgLexicalScanner.Scan(text)
            .Where(t => t.Kind is PgTokenKind.QuotedIdentifier or PgTokenKind.String or PgTokenKind.DollarString)
            .Select(t => text.Substring(t.Start, t.End - t.Start))
            .ToList();

    private static string Normalise(string text)
    {
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return string.Join("\n", text.Split('\n').Select(l => l.TrimEnd()));
    }
}
