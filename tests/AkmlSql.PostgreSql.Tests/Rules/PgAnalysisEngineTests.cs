using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Schema;
using AkmlSql.PostgreSql.Analysis;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Rules;

/// <summary>
/// Spec 042 (task T158, FR-151, FR-154, FR-155) - the engine around the rules: a syntax error is one finding and nothing else;
/// the inline, session, "everywhere" and .casettings suppressions and the severity overrides work as they do for T-SQL; code
/// analysis switched off returns nothing without running.
/// </summary>
[Trait("Category", "Rules")]
public sealed class PgAnalysisEngineTests : IDisposable
{
    private readonly CaSettingsLoader _loader = new();
    private readonly SessionSuppressionStore _session = new();
    private readonly PgAnalysisEngine _engine;

    public PgAnalysisEngineTests() => _engine = new PgAnalysisEngine(new PgRuleRegistry(), _loader, _session);

    public void Dispose() => _loader.Dispose();

    private CodeAnalysisResponse Run(string sql, CodeAnalysisSettings? settings = null, DatabaseCache? cache = null, string? filePath = null, CancellationToken ct = default) =>
        _engine.Analyze(new CodeAnalysisRequest { SessionId = "s1", RequestId = "r1", DocumentText = sql, DocumentVersion = 7, FilePath = filePath }, cache, settings ?? new CodeAnalysisSettings(), ct);

    private static IEnumerable<string> Ids(CodeAnalysisResponse r) => r.Issues.Select(i => i.RuleId);

    // ── FR-154: a script that does not parse ─────────────────────────────────────────────────────────

    [Fact]
    public void A_script_that_does_not_parse_gives_one_syntax_finding_at_the_first_error_and_no_rule_findings()
    {
        var sql = "DELETE FROM customers;\nselec 1;\nDELETE FROM orders;";

        var response = Run(sql);

        var issue = Assert.Single(response.Issues);
        Assert.Equal(PgAnalysisEngine.SyntaxRuleId, issue.RuleId);
        Assert.Equal((int)DiagnosticSeverity.Error, issue.Severity);
        Assert.StartsWith("Syntax error:", issue.Message);
        Assert.Equal(2, issue.Line);
        Assert.Equal(1, issue.Column);
        Assert.Equal("selec", sql.Substring(issue.StartOffset, issue.EndOffset - issue.StartOffset));
        Assert.Equal("r1", response.RequestId);
        Assert.Equal(7, response.AnalyzedVersion);
        Assert.False(string.IsNullOrEmpty(issue.ReferenceUrl));
    }

    [Fact]
    public void Several_errors_still_give_one_finding()
    {
        var response = Run("select 1;\nselec 2;\nselect 3 from;\nselect (4;");

        Assert.Single(response.Issues);
        Assert.Equal(2, response.Issues[0].Line);
    }

    [Fact]
    public void T_SQL_text_is_a_syntax_finding_on_a_PostgreSQL_session_and_runs_no_SQL_Server_rule()
    {
        var response = Run("SELECT TOP 10 * FROM dbo.Orders;\nGO\nDELETE FROM dbo.Orders");

        Assert.Equal(new[] { PgAnalysisEngine.SyntaxRuleId }, Ids(response));
    }

    [Fact]
    public void A_psql_meta_command_is_a_finding_of_its_own_not_a_syntax_error()
    {
        var response = Run("\\dt\nSELECT 1;");

        Assert.Contains("PGEX004", Ids(response));
        Assert.DoesNotContain(PgAnalysisEngine.SyntaxRuleId, Ids(response));
    }

    // ── FR-155: analysis off ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Code_analysis_off_returns_nothing_even_for_text_that_would_not_parse()
    {
        var settings = new CodeAnalysisSettings { Enabled = false };

        Assert.Empty(Run("DELETE FROM orders", settings).Issues);
        Assert.Empty(Run("selec 1", settings).Issues);
    }

    // ── Findings ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Findings_come_in_document_order_and_carry_the_rule_s_description_link_and_fixability()
    {
        var response = Run("SELECT * FROM customers WHERE city != 'x';\nDELETE FROM orders;\n");

        Assert.Equal(response.Issues.OrderBy(i => i.StartOffset).Select(i => i.RuleId), Ids(response));
        var notEquals = response.Issues.Single(i => i.RuleId == "PGST003");
        Assert.True(notEquals.AutoFixable);
        Assert.StartsWith("https://www.postgresql.org/docs/18/", notEquals.ReferenceUrl);
        Assert.Contains("<>", notEquals.Description);
        Assert.Single(notEquals.FixActions);
        Assert.Equal((int)FixType.Transform, notEquals.FixActions[0].FixType);
    }

    [Fact]
    public void The_story_s_example_script_produces_the_four_findings_with_links()
    {
        var name = new string('n', 70);
        var sql = $"CREATE TABLE t ({name} integer, at timestamp);\nDELETE FROM orders;\nBEGIN; CREATE INDEX CONCURRENTLY ON customers (city); COMMIT;";

        var response = Run(sql);

        foreach (var id in new[] { "PGNM001", "PGBP002", "PGEX001", "PGDE001" })
        {
            var issue = Assert.Single(response.Issues, i => i.RuleId == id);
            Assert.StartsWith("https://www.postgresql.org/docs/18/", issue.ReferenceUrl);
            Assert.False(string.IsNullOrWhiteSpace(issue.Description));
        }
    }

    [Fact]
    public void A_rule_that_needs_the_schema_runs_only_with_a_cache()
    {
        const string sql = "SELECT order_id FROM orders WHERE order_id = 1.5;";

        Assert.DoesNotContain("PGPE005", Ids(Run(sql)));
        Assert.Contains("PGPE005", Ids(Run(sql, cache: PgRuleTestKit.Northwind())));
    }

    [Fact]
    public void A_cancelled_run_throws_for_the_handler_to_swallow()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Run("SELECT 1;", ct: cts.Token));
    }

    // ── Severities and "everywhere" ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_global_override_changes_the_severity_of_a_rule()
    {
        var settings = new CodeAnalysisSettings();
        settings.RuleOverrides["PGBP003"] = new RuleOverride { Enabled = true, Severity = "error" };

        var issue = Assert.Single(Run("SELECT * FROM customers, orders WHERE customers.customer_id = orders.customer_id;", settings).Issues, i => i.RuleId == "PGBP003");

        Assert.Equal((int)DiagnosticSeverity.Error, issue.Severity);
        Assert.Equal((int)DiagnosticSeverity.Information, (int)PgRuleTestKit.Registry.Find("PGBP003")!.DefaultSeverity);
    }

    [Fact]
    public void Disabling_a_rule_everywhere_removes_it_and_leaves_the_others()
    {
        var settings = new CodeAnalysisSettings();
        settings.RuleOverrides["pgbp002"] = new RuleOverride { Enabled = false };

        var ids = Ids(Run("DELETE FROM orders;", settings));

        Assert.DoesNotContain("PGBP002", ids);
    }

    [Fact]
    public void A_severity_of_ignore_disables_the_rule()
    {
        var settings = new CodeAnalysisSettings();
        settings.RuleOverrides["PGBP002"] = new RuleOverride { Enabled = true, Severity = "ignore" };

        Assert.DoesNotContain("PGBP002", Ids(Run("DELETE FROM orders;", settings)));
    }

    // ── Inline suppressions (line, range, whole script) ──────────────────────────────────────────────

    [Fact]
    public void Disable_line_removes_the_finding_on_its_own_line_only()
    {
        var response = Run("DELETE FROM customers; -- akml-disable-line PGBP002\nDELETE FROM orders;");

        var issue = Assert.Single(response.Issues, i => i.RuleId == "PGBP002");
        Assert.Equal(2, issue.Line);
    }

    [Fact]
    public void Disable_and_enable_bracket_a_range()
    {
        var response = Run("-- akml-disable PGBP002\nDELETE FROM a;\n-- akml-enable PGBP002\nDELETE FROM b;");

        var issue = Assert.Single(response.Issues, i => i.RuleId == "PGBP002");
        Assert.Equal(4, issue.Line);
    }

    [Fact]
    public void Disable_without_enable_runs_to_the_end_of_the_script()
    {
        var response = Run("DELETE FROM a;\n-- akml-disable PGBP002\nDELETE FROM b;\nDELETE FROM c;");

        var issue = Assert.Single(response.Issues, i => i.RuleId == "PGBP002");
        Assert.Equal(1, issue.Line);
    }

    [Fact]
    public void A_directive_names_its_rules_in_any_case_and_a_block_comment_works()
    {
        Assert.DoesNotContain("PGBP002", Ids(Run("DELETE FROM a; /* akml-disable-line pgbp002 */")));
        Assert.Contains("PGBP002", Ids(Run("DELETE FROM a; -- akml-disable-line PGBP001")));
    }

    [Fact]
    public void The_original_noqa_forms_still_work()
    {
        Assert.DoesNotContain("PGBP002", Ids(Run("DELETE FROM a; -- noqa: PGBP002")));
        Assert.DoesNotContain("PGBP002", Ids(Run("-- noqa-begin\nDELETE FROM a;\n-- noqa-end\n")));
    }

    [Fact]
    public void An_unclosed_noqa_begin_is_reported_as_a_meta_finding()
    {
        var response = Run("-- noqa-begin\nDELETE FROM a;");

        Assert.Contains("NOQA001", Ids(response));
        Assert.DoesNotContain("PGBP002", Ids(response));
    }

    [Fact]
    public void A_directive_inside_a_string_or_a_dollar_body_is_not_a_directive()
    {
        var response = Run("SELECT '-- akml-disable PGBP002'; DELETE FROM a;\nSELECT $$ -- akml-disable PGBP002 $$;");

        Assert.Contains("PGBP002", Ids(response));
    }

    // ── Session suppressions ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_session_suppression_hides_the_rule_until_it_is_lifted()
    {
        const string sql = "DELETE FROM orders;";
        Assert.Contains("PGBP002", Ids(Run(sql)));

        _session.Add("PGBP002");
        Assert.DoesNotContain("PGBP002", Ids(Run(sql)));

        _session.Remove("pgbp002");
        Assert.Contains("PGBP002", Ids(Run(sql)));
    }

    // ── .casettings ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_nearest_casettings_file_applies_to_PostgreSQL_rules_too()
    {
        var dir = Path.Combine(Path.GetTempPath(), "akml-pgrules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, ".casettings"),
                "{ \"rules\": { \"PGBP002\": { \"enabled\": false }, \"PGPE001\": { \"enabled\": true, \"severity\": \"error\" } }, \"globalSuppressions\": [ { \"rule\": \"PGST003\" } ] }");
            var file = Path.Combine(dir, "script.sql");

            var response = Run("DELETE FROM orders; SELECT * FROM customers WHERE 1 != 2;", filePath: file);

            Assert.DoesNotContain("PGBP002", Ids(response));
            Assert.DoesNotContain("PGST003", Ids(response));
            Assert.Equal((int)DiagnosticSeverity.Error, response.Issues.Single(i => i.RuleId == "PGPE001").Severity);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ── Robustness ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   \n\t ")]
    [InlineData("-- just a comment")]
    [InlineData(";;;")]
    [InlineData("/* unterminated")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SELECT $$ unterminated")]
    public void Empty_and_unfinished_text_never_throws(string sql)
    {
        var response = Run(sql);

        Assert.NotNull(response.Issues);
    }
}
