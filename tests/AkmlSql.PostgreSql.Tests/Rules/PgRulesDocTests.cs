using AkmlSql.PostgreSql.Analysis;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Rules;

/// <summary>
/// Spec 042 (task T172, FR-150) - doc/analysis-rules.md lists every PostgreSQL rule, with the severity it ships at, whether it needs the
/// schema, and the manual page it rests on; and lists nothing that is not a rule. The doc is the reference users read, so it cannot drift
/// from the registry.
/// </summary>
[Trait("Category", "Rules")]
public class PgRulesDocTests
{
    private static string DocText()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "doc", "analysis-rules.md"));
    }

    // The PostgreSQL section, from its heading to the next top-level heading.
    private static string Section()
    {
        var doc = DocText();
        var start = doc.IndexOf("## PostgreSQL rules (PG)", StringComparison.Ordinal);
        Assert.True(start >= 0, "doc/analysis-rules.md has no PostgreSQL section");
        var end = doc.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
        return end < 0 ? doc.Substring(start) : doc.Substring(start, end - start);
    }

    private static Dictionary<string, string[]> Rows() =>
        Section().Split('\n')
            .Where(l => l.StartsWith("| PG", StringComparison.Ordinal))
            .Select(l => l.Split('|').Select(c => c.Trim()).ToArray())
            .ToDictionary(cells => cells[1], cells => cells, StringComparer.Ordinal);

    [Fact]
    public void The_doc_lists_every_rule_exactly_once_and_nothing_else()
    {
        var rows = Rows();
        var ids = new PgRuleRegistry().AllRules.Select(r => r.RuleId).OrderBy(i => i, StringComparer.Ordinal).ToArray();

        Assert.Equal(ids, rows.Keys.OrderBy(i => i, StringComparer.Ordinal).ToArray());
        Assert.Equal(ids.Length, Section().Split('\n').Count(l => l.StartsWith("| PG", StringComparison.Ordinal)));
    }

    [Fact]
    public void Each_row_states_the_rule_s_severity_schema_need_and_manual_link()
    {
        var rows = Rows();

        foreach (var rule in new PgRuleRegistry().AllRules)
        {
            var cells = rows[rule.RuleId];
            Assert.Equal(rule.DefaultSeverity.ToString(), cells[2]);
            Assert.Equal(rule.RequiresSchema ? "required" : "—", cells[3]);
            Assert.Equal(rule.AutoFixable ? "yes" : "—", cells[4]);
            Assert.Contains(rule.Name, cells[5]);
            Assert.Contains(rule.Description.Replace("|", "&#124;"), cells[5]);      // the doc says what the rule says
            Assert.Contains("(" + rule.ReferenceUrl + ")", cells[5]);
        }
    }

    [Fact]
    public void The_section_says_how_many_rules_there_are_and_how_a_syntax_error_is_reported()
    {
        var section = Section();

        Assert.Contains("52 rules", section);
        Assert.Contains("PGSYN001", section);
        Assert.Contains("dialect.postgresql.v1", section);
    }
}
