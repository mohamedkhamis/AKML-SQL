namespace AkmlSql.Engine.Ai.Prompts;

/// <summary>
/// Builds the system prompt for the AI Chat panel (Phase 9, US6 / T049).
/// The system prompt instructs the AI to act as an integrated SQL assistant,
/// helping with queries, optimization, schema questions, and best practices.
/// </summary>
public static class ChatSystemPrompt
{
    private const string TaskInstruction = """
        You are an AI SQL assistant integrated into SQL Server Management Studio.
        Help the user with SQL queries, performance optimization, schema questions,
        and database best practices. When suggesting code changes, wrap SQL in ```sql
        code blocks. Be concise and practical.
        """;

    // Spec 042 (FR-080): the PostgreSQL wording. Nothing here names SQL Server or its tools.
    private const string PostgreSqlTaskInstruction = """
        You are an AI SQL assistant integrated into a PostgreSQL query editor.
        Help the user with PostgreSQL queries, performance optimization, schema questions,
        and database best practices. When suggesting code changes, wrap SQL in ```sql
        code blocks. Be concise and practical.
        """;

    /// <summary>
    /// Builds the full system message for the chat panel, including schema context.
    /// </summary>
    /// <param name="schemaText">Formatted schema context from <see cref="Context.SchemaContextFormatter"/>.</param>
    /// <param name="dialect">The SQL language of the active connection (spec 042). SQL Server by default.</param>
    /// <param name="serverVersion">The PostgreSQL server version when known; ignored for SQL Server.</param>
    /// <returns>The complete system prompt string.</returns>
    public static string Build(string schemaText, SqlDialect dialect = SqlDialect.SqlServer, string? serverVersion = null)
    {
        return PromptTemplates.BuildSystemPrompt(
            schemaText,
            dialect == SqlDialect.PostgreSql ? PostgreSqlTaskInstruction : TaskInstruction,
            dialect,
            serverVersion);
    }
}
