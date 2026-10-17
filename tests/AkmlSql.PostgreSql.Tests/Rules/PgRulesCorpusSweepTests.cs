using AkmlSql.Engine.Analysis;
using AkmlSql.PostgreSql.Analysis;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Rules;

/// <summary>
/// Spec 042 (task T159, SC-016) - every rule over every script of the format corpus: no rule throws, nothing is reported at an
/// impossible position, and no finding carries a SQL Server rule id. The corpus is valid PostgreSQL written to exercise the
/// formatter, so it is also a check that the rules do not mistake ordinary SQL for a problem they cannot place.
/// </summary>
[Trait("Category", "Rules")]
public class PgRulesCorpusSweepTests
{
    private static string CorpusDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pg-format-corpus", "corpus");
        Assert.True(Directory.Exists(dir), "The format corpus was not copied next to the tests: " + dir);
        return dir;
    }

    public static IEnumerable<object[]> Scripts() =>
        Directory.EnumerateFiles(CorpusDirectory(), "*.sql").OrderBy(f => f, StringComparer.Ordinal).Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [Fact]
    public void The_corpus_has_at_least_one_hundred_scripts()
    {
        Assert.True(Directory.EnumerateFiles(CorpusDirectory(), "*.sql").Count() >= 100);
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Every_rule_runs_on_the_script_without_a_crash_and_places_its_findings_inside_it(string name)
    {
        var sql = File.ReadAllText(Path.Combine(CorpusDirectory(), name + ".sql"));
        var context = PgAnalysisContext.Create(sql, PgRuleTestKit.Northwind());
        Assert.Null(context.FirstSyntaxError);

        var tsqlIds = new RuleRegistry().AllRules.Select(r => r.RuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in PgRuleTestKit.Registry.AllRules)
        {
            var found = rule.Analyze(context).ToList();       // an exception here fails the test, naming the rule in the trace

            foreach (var d in found)
            {
                Assert.Equal(rule.RuleId, d.RuleId);
                Assert.DoesNotContain(d.RuleId, tsqlIds);
                Assert.InRange(d.StartOffset, 0, sql.Length);
                Assert.InRange(d.EndOffset, d.StartOffset, sql.Length);
                Assert.True(d.Line >= 1 && d.Column >= 1, $"{rule.RuleId} line {d.Line} column {d.Column}");
                Assert.False(string.IsNullOrWhiteSpace(d.Message), rule.RuleId);
                Assert.All(d.FixActions, f => Assert.InRange(f.ReplacementEnd, f.ReplacementStart, sql.Length));
            }
        }
    }

    [Fact]
    public void A_script_of_two_thousand_statements_is_analysed_in_seconds_not_minutes()
    {
        // Statement by statement parsing and per-statement tokens keep the cost linear; a quadratic step would show here first.
        var scripts = Directory.EnumerateFiles(CorpusDirectory(), "*.sql").Select(File.ReadAllText).Where(s => PgAnalysisContext.Create(s).FirstSyntaxError == null).ToList();
        var big = string.Join('\n', Enumerable.Range(0, 14).SelectMany(_ => scripts));
        using var loader = new CaSettingsLoader();
        var engine = new PgAnalysisEngine(new PgRuleRegistry(), loader);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var response = engine.Analyze(
            new AkmlSql.Core.Ipc.Messages.CodeAnalysisRequest { SessionId = "s", RequestId = "r", DocumentText = big },
            PgRuleTestKit.Northwind(), new AkmlSql.Core.Config.CodeAnalysisSettings(), CancellationToken.None);
        clock.Stop();

        var lines = big.Split('\n').Length;
        Assert.True(lines > 2000, "the script should be over two thousand lines: " + lines);
        Assert.NotEmpty(response.Issues);
        Assert.True(clock.ElapsedMilliseconds < 5000, $"analysing {big.Length:N0} characters took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void The_engine_over_the_whole_corpus_reports_only_PostgreSQL_rule_ids_and_no_syntax_errors()
    {
        using var loader = new CaSettingsLoader();
        var engine = new PgAnalysisEngine(new PgRuleRegistry(), loader);
        var known = PgRuleTestKit.Registry.AllRules.Select(r => r.RuleId).ToHashSet(StringComparer.Ordinal);
        var tsqlIds = new RuleRegistry().AllRules.Select(r => r.RuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var total = 0;

        foreach (var file in Directory.EnumerateFiles(CorpusDirectory(), "*.sql"))
        {
            var response = engine.Analyze(
                new AkmlSql.Core.Ipc.Messages.CodeAnalysisRequest { SessionId = "s", RequestId = "r", DocumentText = File.ReadAllText(file) },
                PgRuleTestKit.Northwind(), new AkmlSql.Core.Config.CodeAnalysisSettings(), CancellationToken.None);

            foreach (var issue in response.Issues)
            {
                total++;
                Assert.DoesNotContain(issue.RuleId, tsqlIds);
                Assert.True(known.Contains(issue.RuleId) || issue.RuleId == "NOQA001", Path.GetFileName(file) + ": " + issue.RuleId);
                Assert.NotEqual(PgAnalysisEngine.SyntaxRuleId, issue.RuleId);
            }
        }

        Assert.True(total > 0, "A corpus of 150 scripts should trip at least one rule.");
    }
}
