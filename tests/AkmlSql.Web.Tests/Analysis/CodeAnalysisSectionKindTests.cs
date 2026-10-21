using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Analysis;

/// <summary>
/// Spec 042 (tasks T160, T172, FR-152). Settings › Code analysis lists every rule grouped by the database it is for - SQL Server and
/// PostgreSQL - and then by category, with each rule's severity (a browser-local remap applied) and, for a PostgreSQL rule, the link to the
/// manual page it rests on. A rule suppressed everywhere is marked, and is named whichever set it belongs to. Without an engine the
/// PostgreSQL group says why it is empty.
/// </summary>
public sealed class CodeAnalysisSectionKindTests : IDisposable
{
    private sealed class Rules : IPostgresAnalysisEngine
    {
        public IReadOnlyList<AnalysisRuleInfoDto> List { get; set; } = new[]
        {
            Rule("PGPE001", "SELECT *", "Performance", DiagnosticSeverity.Warning),
            Rule("PGPE002", "Leading wildcard in LIKE", "Performance", DiagnosticSeverity.Warning),
            Rule("PGBP002", "UPDATE or DELETE without WHERE", "BestPractices", DiagnosticSeverity.Error),
            Rule("PGNM001", "Identifier longer than 63 bytes", "Naming", DiagnosticSeverity.Warning),
        };

        public static AnalysisRuleInfoDto Rule(string id, string name, string category, DiagnosticSeverity severity) => new()
        {
            RuleId = id, Name = name, Category = category, DefaultSeverity = (int)severity, EffectiveSeverity = (int)severity, Enabled = true,
            Kind = 1, Description = name, ReferenceUrl = "https://www.postgresql.org/docs/18/sql-select.html",
        };

        public Task<CodeAnalysisResponse> AnalyseAsync(string documentText, CancellationToken ct) =>
            Task.FromResult(new CodeAnalysisResponse { Issues = Array.Empty<CodeIssueInfo>() });

        public Task<IReadOnlyList<AnalysisRuleInfoDto>> ListRulesAsync(CancellationToken ct) => Task.FromResult(List);
    }

    private readonly SettingsPageHost _host = new();

    public void Dispose() => _host.Dispose();

    private IRenderedComponent<AkmlSql.Web.Pages.Settings> Open(Rules? rules)
    {
        if (rules != null) _host.Ctx.Services.AddSingleton<IPostgresAnalysisEngine>(rules);
        return _host.Open("settings/code-analysis");
    }

    [Fact]
    public void The_rules_are_grouped_by_database_then_by_category()
    {
        var cut = Open(new Rules());

        var kinds = cut.FindAll("[data-testid^=rules-kind-][data-kind]").Select(e => e.GetAttribute("data-kind")).ToArray();
        Assert.Equal(new[] { "sqlserver", "postgresql" }, kinds);

        var pg = cut.Find("[data-testid=rules-kind-postgresql]");
        var categories = pg.QuerySelectorAll("details").Select(d => d.GetAttribute("data-testid")).ToArray();
        Assert.Equal(new[] { "rules-category-postgresql-Performance", "rules-category-postgresql-BestPractices", "rules-category-postgresql-Naming" }, categories);
        Assert.Contains("Best practices", pg.QuerySelector("[data-testid=rules-category-postgresql-BestPractices] summary")!.TextContent);
        Assert.Contains("(4 rules)", pg.QuerySelector("h4")!.TextContent);

        var sql = cut.Find("[data-testid=rules-kind-sqlserver]");
        Assert.True(sql.QuerySelectorAll("details").Length >= 7, "the T-SQL rules are grouped by category too");
        Assert.NotNull(sql.QuerySelector("[data-testid=rule-PE001]"));
        Assert.Null(sql.QuerySelector("[data-testid=rule-PGPE001]"));
        Assert.Null(pg.QuerySelector("[data-testid=rule-PE001]"));
    }

    [Fact]
    public void A_PostgreSQL_rule_shows_its_name_its_severity_and_the_link_to_the_manual()
    {
        var cut = Open(new Rules());

        var rule = cut.Find("[data-testid=rule-PGBP002]");

        Assert.Contains("UPDATE or DELETE without WHERE", rule.TextContent);
        Assert.Equal("Error", rule.GetAttribute("data-severity"));
        var link = rule.QuerySelector("a")!;
        Assert.StartsWith("https://www.postgresql.org/docs/18/", link.GetAttribute("href"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Fact]
    public async Task A_rule_turned_off_everywhere_is_marked_and_a_remapped_one_shows_its_new_severity()
    {
        await _host.Stores.Analysis.SetAsync(new WebAnalysisSettings
        {
            RuleOverrides = new Dictionary<string, string> { ["PGPE001"] = "off", ["PGNM001"] = "error", ["PE001"] = "off" },
        });

        var cut = Open(new Rules());

        var off = cut.Find("[data-testid=rule-PGPE001]");
        Assert.Equal("true", off.GetAttribute("data-off"));
        Assert.Contains("off everywhere", off.TextContent);
        Assert.Equal("Error", cut.Find("[data-testid=rule-PGNM001]").GetAttribute("data-severity"));
        Assert.Equal("true", cut.Find("[data-testid=rule-PE001]").GetAttribute("data-off"));
        Assert.Null(cut.Find("[data-testid=rule-PGPE002]").GetAttribute("data-off"));
    }

    [Fact]
    public async Task The_suppressed_list_names_a_PostgreSQL_rule_and_says_which_database_it_is_for()
    {
        await _host.Stores.Analysis.SetAsync(new WebAnalysisSettings
        {
            RuleOverrides = new Dictionary<string, string> { ["PGBP002"] = "off", ["PE001"] = "off" },
        });

        var cut = Open(new Rules());

        var items = cut.FindAll("[data-testid^=suppressed-rule-][data-kind]");
        Assert.Equal(new[] { "sqlserver", "postgresql" }, items.Select(i => i.GetAttribute("data-kind")).ToArray());
        Assert.Contains("UPDATE or DELETE without WHERE", cut.Find("[data-testid=suppressed-rule-PGBP002]").TextContent);
    }

    [Fact]
    public void Without_an_engine_the_PostgreSQL_group_is_empty_and_says_why()
    {
        var cut = Open(null);

        var pg = cut.Find("[data-testid=rules-kind-postgresql]");

        Assert.Empty(pg.QuerySelectorAll("details"));
        Assert.Contains("(0 rules)", pg.QuerySelector("h4")!.TextContent);
        Assert.Contains("engine", cut.Find("[data-testid=rules-kind-postgresql-note]").TextContent);
        Assert.NotEmpty(cut.Find("[data-testid=rules-kind-sqlserver]").QuerySelectorAll("details"));
    }

    [Fact]
    public void An_engine_that_lists_nothing_is_treated_as_no_engine()
    {
        var cut = Open(new Rules { List = Array.Empty<AnalysisRuleInfoDto>() });

        Assert.NotNull(cut.Find("[data-testid=rules-kind-postgresql-note]"));
    }
}
