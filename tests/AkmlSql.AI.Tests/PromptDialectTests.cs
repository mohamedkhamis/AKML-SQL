using AkmlSql.Core;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Ai.Prompts;
using Xunit;

namespace AkmlSql.AI.Tests;

/// <summary>
/// Spec 042 (task T110, FR-080, FR-082, FR-090, SC-011). The AI prompts name the dialect of the active connection.
/// The SQL Server text is pinned to files captured from the code before the dialect existed, so a change to it is
/// a deliberate edit of a golden file and never an accident. The PostgreSQL text must name PostgreSQL, forbid T-SQL
/// and carry nothing that belongs to SQL Server.
/// </summary>
public sealed class PromptDialectTests
{
    private const string Schema = "Tables: dbo.Customers (Id, Name), dbo.Orders (Id, CustomerId, Total)";
    private const string Sql = "SELECT Id FROM dbo.Customers;";
    private const string UserSeparator = "\n=====USER=====\n";

    // The words are pinned, not the checkout's line endings: a Windows checkout turns both the sources and these files
    // into CRLF, a Linux one into LF, and the two always change together.
    private static string Lf(string text) => text.Replace("\r\n", "\n");

    private static void AssertGolden(string name, string actual) =>
        Assert.Equal(Lf(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name))), Lf(actual));

    private static string Joined((string systemMessage, string userMessage) pair) =>
        pair.systemMessage + UserSeparator + pair.userMessage;

    // ---- SQL Server text does not change -----------------------------------------------------------------------

    [Fact]
    public void The_SqlServer_system_prompt_equals_the_golden_file_byte_for_byte()
    {
        AssertGolden("system-prompt-sqlserver.golden.txt", PromptTemplates.BuildSystemPrompt(Schema, "TASK-INSTRUCTION"));
    }

    [Fact]
    public void The_default_dialect_is_SqlServer()
    {
        Assert.Equal(
            PromptTemplates.BuildSystemPrompt(Schema, "X", SqlDialect.SqlServer),
            PromptTemplates.BuildSystemPrompt(Schema, "X"));
    }

    [Fact]
    public void Every_SqlServer_builder_equals_its_golden_file_byte_for_byte()
    {
        AssertGolden("prompt-chat-sqlserver.golden.txt", ChatSystemPrompt.Build(Schema));
        AssertGolden("prompt-explain-sqlserver.golden.txt", Joined(ExplainPrompt.Build(Schema, Sql)));
        AssertGolden("prompt-fix-sqlserver.golden.txt", Joined(FixPrompt.Build(Schema, "SELEC 1", "Incorrect syntax near 'SELEC'.", 102)));
        AssertGolden("prompt-ghost-sqlserver.golden.txt", Joined(GhostTextPrompt.Build(Schema, "SELECT * FROM ")));
        AssertGolden("prompt-index-sqlserver.golden.txt", Joined(IndexAnalysisPrompt.Build(Schema, Sql, "<ShowPlanXML/>")));
        AssertGolden("prompt-optimize-sqlserver.golden.txt", Joined(OptimizePrompt.Build(Schema, Sql)));
        AssertGolden("prompt-text2sql-sqlserver.golden.txt", Joined(TextToSqlPrompt.Build(Schema, "the three most recent orders per customer")));
    }

    [Fact]
    public void Naming_SqlServer_explicitly_changes_nothing_and_a_server_version_is_not_added()
    {
        AssertGolden("prompt-chat-sqlserver.golden.txt", ChatSystemPrompt.Build(Schema, SqlDialect.SqlServer, "16.0"));
        AssertGolden(
            "prompt-fix-sqlserver.golden.txt",
            Joined(FixPrompt.Build(Schema, "SELEC 1", "Incorrect syntax near 'SELEC'.", 102, SqlDialect.SqlServer, "16.0", sqlState: "42601", detail: "d", hint: "h")));
    }

    // ---- PostgreSQL text ---------------------------------------------------------------------------------------

    [Fact]
    public void The_PostgreSql_system_prompt_names_PostgreSQL_and_forbids_T_SQL()
    {
        var prompt = PromptTemplates.BuildSystemPrompt(Schema, "TASK-INSTRUCTION", SqlDialect.PostgreSql);

        Assert.Contains("PostgreSQL", prompt);
        Assert.Contains("TASK-INSTRUCTION", prompt);
        Assert.Contains(Schema, prompt);
        Assert.Contains("Do not use T-SQL", prompt);
        Assert.DoesNotContain("SQL Server", prompt);
        Assert.DoesNotContain("You work with T-SQL", prompt);
        Assert.DoesNotContain("Use T-SQL syntax", prompt);
    }

    [Fact]
    public void The_PostgreSql_system_prompt_tells_the_model_the_server_version()
    {
        Assert.Contains("PostgreSQL 16.4", PromptTemplates.BuildSystemPrompt(Schema, "X", SqlDialect.PostgreSql, "16.4"));
        // a version that already reads "PostgreSQL ..." is not doubled
        var already = PromptTemplates.BuildSystemPrompt(Schema, "X", SqlDialect.PostgreSql, "PostgreSQL 18.1");
        Assert.Contains("PostgreSQL 18.1", already);
        Assert.DoesNotContain("PostgreSQL PostgreSQL", already);
        // no version known: nothing is invented
        Assert.DoesNotContain("The server is", PromptTemplates.BuildSystemPrompt(Schema, "X", SqlDialect.PostgreSql));
    }

    [Fact]
    public void The_PostgreSql_rules_steer_to_PostgreSQL_forms()
    {
        var prompt = PromptTemplates.BuildSystemPrompt(Schema, "X", SqlDialect.PostgreSql);

        Assert.Contains("LIMIT", prompt);
        Assert.Contains("double quotes", prompt);
        Assert.Contains("COALESCE", prompt);
    }

    public static IEnumerable<object[]> PostgreSqlPrompts()
    {
        yield return new object[] { "chat", ChatSystemPrompt.Build(Schema, SqlDialect.PostgreSql, "16.4") };
        yield return new object[] { "explain", ExplainPrompt.Build(Schema, Sql, SqlDialect.PostgreSql, "16.4").systemMessage };
        yield return new object[] { "fix", FixPrompt.Build(Schema, "SELEC 1", "syntax error at or near \"SELEC\"", 0, SqlDialect.PostgreSql, "16.4", sqlState: "42601").systemMessage };
        yield return new object[] { "ghost", GhostTextPrompt.Build(Schema, "SELECT * FROM ", SqlDialect.PostgreSql, "16.4").systemMessage };
        yield return new object[] { "index", IndexAnalysisPrompt.Build(Schema, Sql, null, SqlDialect.PostgreSql, "16.4").systemMessage };
        yield return new object[] { "optimize", OptimizePrompt.Build(Schema, Sql, SqlDialect.PostgreSql, "16.4").systemMessage };
        yield return new object[] { "text2sql", TextToSqlPrompt.Build(Schema, "orders per customer", SqlDialect.PostgreSql, "16.4").systemMessage };
    }

    [Theory]
    [MemberData(nameof(PostgreSqlPrompts))]
    public void Every_PostgreSql_prompt_names_PostgreSQL_and_carries_nothing_of_SQL_Server(string feature, string system)
    {
        Assert.True(system.Contains("PostgreSQL"), $"{feature}: does not name PostgreSQL");
        Assert.DoesNotContain("SQL Server", system);
        Assert.DoesNotContain("Management Studio", system);
        Assert.DoesNotContain("valid T-SQL", system);
        Assert.DoesNotContain("improved T-SQL", system);
        Assert.DoesNotContain("[IX_Name]", system);
        Assert.DoesNotContain("[schema].[table]", system);
        Assert.Contains("Do not use T-SQL", system);
    }

    [Fact]
    public void The_PostgreSql_fix_prompt_carries_SQLSTATE_detail_and_hint()
    {
        var (_, user) = FixPrompt.Build(
            Schema, "SELECT * FROM custmers", "relation \"custmers\" does not exist", 0, SqlDialect.PostgreSql, "16.4",
            sqlState: "42P01", detail: "The table was dropped yesterday.", hint: "Perhaps you meant customers.");

        Assert.Contains("SQLSTATE: 42P01", user);
        Assert.Contains("relation \"custmers\" does not exist", user);
        Assert.Contains("Detail: The table was dropped yesterday.", user);
        Assert.Contains("Hint: Perhaps you meant customers.", user);
        Assert.Contains("SELECT * FROM custmers", user);
        Assert.DoesNotContain("Error Number", user);
    }

    [Fact]
    public void The_PostgreSql_fix_prompt_leaves_out_what_the_server_did_not_send()
    {
        var (_, user) = FixPrompt.Build(Schema, "SELEC 1", "syntax error", 0, SqlDialect.PostgreSql, null, sqlState: "42601");

        Assert.Contains("SQLSTATE: 42601", user);
        Assert.DoesNotContain("Detail:", user);
        Assert.DoesNotContain("Hint:", user);
    }

    [Fact]
    public void The_PostgreSql_fix_prompt_asks_for_PostgreSQL_that_runs_directly()
    {
        var (system, _) = FixPrompt.Build(Schema, "SELEC 1", "syntax error", 0, SqlDialect.PostgreSql, null, sqlState: "42601");

        Assert.Contains("FIXED SQL:", system);
        Assert.Contains("EXPLANATION:", system);
        Assert.Contains("valid PostgreSQL", system);
    }

    [Fact]
    public void The_PostgreSql_index_and_optimize_prompts_ask_for_PostgreSQL_index_syntax_and_keep_the_section_headers()
    {
        var (index, _) = IndexAnalysisPrompt.Build(Schema, Sql, null, SqlDialect.PostgreSql);
        var (optimize, _) = OptimizePrompt.Build(Schema, Sql, SqlDialect.PostgreSql);

        // the parsers read these headers for both dialects
        Assert.Contains("SUMMARY:", index);
        Assert.Contains("INDEX SUGGESTIONS:", index);
        Assert.Contains("OPTIMIZED SQL:", optimize);
        Assert.Contains("EXPLANATION:", optimize);
        Assert.Contains("ANNOTATIONS:", optimize);
        Assert.Contains("INDEX SUGGESTIONS:", optimize);
        Assert.Contains("[SAFE]", optimize);
        Assert.Contains("[REVIEW]", optimize);
        Assert.Contains("CREATE INDEX ix_", index);
        Assert.Contains("CREATE INDEX ix_", optimize);
    }

    [Fact]
    public void The_PostgreSql_index_prompt_still_sends_the_plan_when_there_is_one()
    {
        var (_, user) = IndexAnalysisPrompt.Build(Schema, Sql, "Seq Scan on customers", SqlDialect.PostgreSql);

        Assert.Contains(Sql, user);
        Assert.Contains("Seq Scan on customers", user);
    }

    [Fact]
    public void The_user_messages_are_the_same_for_both_dialects_where_nothing_differs()
    {
        Assert.Equal(ExplainPrompt.Build(Schema, Sql).userMessage, ExplainPrompt.Build(Schema, Sql, SqlDialect.PostgreSql).userMessage);
        Assert.Equal(GhostTextPrompt.Build(Schema, "SELECT ").userMessage, GhostTextPrompt.Build(Schema, "SELECT ", SqlDialect.PostgreSql).userMessage);
        Assert.Equal(TextToSqlPrompt.Build(Schema, "x").userMessage, TextToSqlPrompt.Build(Schema, "x", SqlDialect.PostgreSql).userMessage);
        Assert.Equal(OptimizePrompt.Build(Schema, Sql).userMessage, OptimizePrompt.Build(Schema, Sql, SqlDialect.PostgreSql).userMessage);
    }

    // ---- the mapping from the connection kind ----------------------------------------------------------------

    [Theory]
    [InlineData(DatabaseKind.SqlServer, SqlDialect.SqlServer)]
    [InlineData(DatabaseKind.PostgreSql, SqlDialect.PostgreSql)]
    [InlineData((DatabaseKind)99, SqlDialect.SqlServer)]
    public void A_connection_kind_maps_to_a_dialect_and_an_unknown_kind_is_SqlServer(DatabaseKind kind, SqlDialect expected)
    {
        Assert.Equal(expected, SqlDialects.From(kind));
    }
}
