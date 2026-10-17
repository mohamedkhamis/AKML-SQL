using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.PostgreSql.Analysis;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Rules;

/// <summary>
/// Spec 042 (task T157, FR-150, FR-152, SC-016) - the rule set as a whole: at least 40 rules, a distinct <c>PG</c> id each with a
/// category, a documented behaviour and a link into the PostgreSQL 18 manual, and no overlap with the T-SQL rule set in either direction.
/// </summary>
[Trait("Category", "Rules")]
public class PgRuleRegistryTests
{
    private static readonly string[] Categories = { "Performance", "BestPractices", "Security", "Style", "Design", "Deprecated", "Execution", "Naming" };

    private static readonly Dictionary<string, string> CodeOfCategory = new()
    {
        ["Performance"] = "PGPE", ["BestPractices"] = "PGBP", ["Security"] = "PGSE", ["Style"] = "PGST",
        ["Design"] = "PGDE", ["Deprecated"] = "PGDEP", ["Execution"] = "PGEX", ["Naming"] = "PGNM",
    };

    private static readonly IReadOnlyList<IPgAnalysisRule> Rules = new PgRuleRegistry().AllRules;

    [Fact]
    public void The_release_ships_at_least_forty_rules_and_the_catalogue_lists_fifty_two()
    {
        Assert.True(Rules.Count >= 40, "FR-150 asks for at least 40 rules; found " + Rules.Count);
        Assert.Equal(52, Rules.Count);
    }

    [Fact]
    public void Rule_ids_are_unique_and_carry_the_PG_prefix_a_category_code_and_three_digits()
    {
        Assert.Equal(Rules.Count, Rules.Select(r => r.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.All(Rules, r =>
        {
            Assert.Matches("^PG[A-Z]{2,3}[0-9]{3}$", r.RuleId);
            Assert.Equal(CodeOfCategory[r.Category], r.CategoryCode);
            Assert.StartsWith(r.CategoryCode, r.RuleId);
        });
    }

    [Fact]
    public void Every_rule_has_a_name_a_description_a_default_severity_and_a_manual_link()
    {
        Assert.All(Rules, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Name), r.RuleId + " name");
            Assert.True(r.Description.Length > 40, r.RuleId + " description");
            Assert.True(Enum.IsDefined(typeof(DiagnosticSeverity), r.DefaultSeverity), r.RuleId + " severity");
            Assert.True(Uri.TryCreate(r.ReferenceUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps, r.RuleId + " reference " + r.ReferenceUrl);
        });

        // FR-150: every rule rests on the manual, except the one product convention.
        Assert.All(Rules.Where(r => r.RuleId != "PGST005"), r => Assert.StartsWith("https://www.postgresql.org/docs/18/", r.ReferenceUrl));
    }

    [Fact]
    public void The_rules_cover_the_eight_categories_with_the_counts_of_the_catalogue()
    {
        var counts = Rules.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(Categories.OrderBy(c => c), counts.Keys.OrderBy(c => c));
        Assert.Equal(10, counts["Performance"]);
        Assert.Equal(10, counts["BestPractices"]);
        Assert.Equal(5, counts["Security"]);
        Assert.Equal(5, counts["Style"]);
        Assert.Equal(8, counts["Design"]);
        Assert.Equal(4, counts["Deprecated"]);
        Assert.Equal(5, counts["Execution"]);
        Assert.Equal(5, counts["Naming"]);
    }

    [Fact]
    public void The_schema_aware_rules_are_the_ones_the_catalogue_marks()
    {
        Assert.Equal(new[] { "PGBP005", "PGDE006", "PGPE004", "PGPE005" }, Rules.Where(r => r.RequiresSchema).Select(r => r.RuleId).OrderBy(i => i).ToArray());
    }

    [Fact]
    public void The_rules_named_by_the_functional_requirement_are_all_present()
    {
        // FR-153: truncated names, folded names, reserved words, SELECT *, write without WHERE, leading wildcard, function on a column,
        // NOT IN with a nullable subquery, timestamp, char(n), money, serial, no primary key, unindexed foreign key, no transaction block,
        // dynamic SQL by concatenation, broad GRANT, keyword case, missing terminators.
        var required = new[]
        {
            "PGNM001", "PGNM002", "PGNM003", "PGPE001", "PGBP002", "PGPE002", "PGPE003", "PGPE004", "PGDE001", "PGDE002", "PGDE003",
            "PGDE004", "PGDE005", "PGDE006", "PGEX001", "PGSE001", "PGSE002", "PGST001", "PGST002",
        };
        Assert.Empty(required.Except(Rules.Select(r => r.RuleId)));
    }

    [Fact]
    public void Every_rule_has_a_positive_and_a_negative_test_case()
    {
        Assert.Equal(Rules.Select(r => r.RuleId).OrderBy(i => i), PgRuleCases.All.Select(c => c.RuleId).OrderBy(i => i));
        Assert.All(PgRuleCases.All, c =>
        {
            Assert.Contains("[[", c.Positive);
            Assert.NotEmpty(c.Negatives);
        });
    }

    [Fact]
    public void The_PostgreSQL_and_T_SQL_rule_sets_share_nothing()
    {
        var tsql = new RuleRegistry().AllRules;

        Assert.Empty(Rules.Select(r => r.RuleId).Intersect(tsql.Select(r => r.RuleId), StringComparer.OrdinalIgnoreCase));
        Assert.All(tsql, r => Assert.False(r.RuleId.StartsWith("PG", StringComparison.OrdinalIgnoreCase), r.RuleId));

        // A PostgreSQL rule is not an IAnalysisRule and a T-SQL rule is not an IPgAnalysisRule, so neither registry can hand one the other's text.
        Assert.All(Rules, r => Assert.False(r is IAnalysisRule));
        Assert.All(tsql, r => Assert.False(r is IPgAnalysisRule));
    }

    [Fact]
    public void A_rule_is_disabled_by_the_settings_in_effect()
    {
        var registry = new PgRuleRegistry();
        var settings = new ResolvedAnalysisSettings();
        settings.EffectiveRules["pgbp002"] = new ResolvedRuleConfig { Enabled = false };

        var enabled = registry.GetEnabledRules(settings);

        Assert.Equal(registry.AllRules.Count - 1, enabled.Count);
        Assert.DoesNotContain(enabled, r => r.RuleId == "PGBP002");
    }
}
