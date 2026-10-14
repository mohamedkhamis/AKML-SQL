namespace AkmlSql.Engine.Ai.Prompts;

/// <summary>
/// Builds system and user messages for AI-powered SQL error fix suggestions (Phase 9, US3).
/// The system prompt instructs the AI to return the corrected SQL followed by an explanation
/// of what was wrong, using structured FIXED SQL and EXPLANATION sections.
/// </summary>
public static class FixPrompt
{
    private const string TaskInstruction = """
        Fix the following SQL query that produced an error. Return the corrected SQL first,
        then explain what was wrong. Structure your response with these exact sections:
        FIXED SQL: (the corrected query — raw SQL only, no markdown code fences)
        EXPLANATION: (what was wrong and what you changed)

        Use only the section headers shown above. Do not use markdown formatting or code fences.
        The FIXED SQL section must contain only valid T-SQL that can be executed directly.
        """;

    // Spec 042 (FR-080, FR-082): same section headers (the response parser reads them), PostgreSQL wording.
    private const string PostgreSqlTaskInstruction = """
        Fix the following SQL query that produced an error. Return the corrected SQL first,
        then explain what was wrong. Structure your response with these exact sections:
        FIXED SQL: (the corrected query — raw SQL only, no markdown code fences)
        EXPLANATION: (what was wrong and what you changed)

        Use only the section headers shown above. Do not use markdown formatting or code fences.
        The FIXED SQL section must contain only valid PostgreSQL that can be executed directly.
        The error comes with its SQLSTATE code and, when the server sent them, a detail and a hint: use them.
        """;

    /// <summary>
    /// Builds the system and user messages for an AI Fix prompt.
    /// </summary>
    /// <param name="schemaText">Formatted schema context from <see cref="Context.SchemaContextFormatter"/>.</param>
    /// <param name="failingSql">The SQL text that produced an error.</param>
    /// <param name="errorMessage">The error message returned by SQL Server.</param>
    /// <param name="errorNumber">The SQL Server error number (e.g. 208 for invalid object name). Not used for PostgreSQL.</param>
    /// <param name="dialect">The SQL language of the active connection (spec 042). SQL Server by default.</param>
    /// <param name="serverVersion">The PostgreSQL server version when known; ignored for SQL Server.</param>
    /// <param name="sqlState">PostgreSQL only: the five-character SQLSTATE code of the error (for example <c>42P01</c>).</param>
    /// <param name="detail">PostgreSQL only: the server's detail text, when it sent one.</param>
    /// <param name="hint">PostgreSQL only: the server's hint text, when it sent one.</param>
    /// <returns>A tuple of (systemMessage, userMessage).</returns>
    public static (string systemMessage, string userMessage) Build(
        string schemaText,
        string failingSql,
        string errorMessage,
        int errorNumber,
        SqlDialect dialect = SqlDialect.SqlServer,
        string? serverVersion = null,
        string? sqlState = null,
        string? detail = null,
        string? hint = null)
    {
        if (dialect == SqlDialect.PostgreSql)
        {
            return (
                PromptTemplates.BuildSystemPrompt(schemaText, PostgreSqlTaskInstruction, dialect, serverVersion),
                BuildPostgreSqlUserMessage(failingSql, errorMessage, sqlState, detail, hint));
        }

        var system = PromptTemplates.BuildSystemPrompt(schemaText, TaskInstruction);

        var user = $"""
            The following SQL query failed with an error.

            Error Number: {errorNumber}
            Error Message: {errorMessage}

            Failing SQL:
            {failingSql}
            """;

        return (system, user);
    }

    /// <summary>
    /// Spec 042 (FR-082): the error block of a PostgreSQL failure. PostgreSQL has no error number; the SQLSTATE code,
    /// message, detail and hint are what tell the model what went wrong, so each is sent when the server provided it.
    /// </summary>
    private static string BuildPostgreSqlUserMessage(
        string failingSql, string errorMessage, string? sqlState, string? detail, string? hint)
    {
        var text = new System.Text.StringBuilder("The following SQL query failed with an error.\n\n");
        if (!string.IsNullOrWhiteSpace(sqlState)) text.Append("SQLSTATE: ").Append(sqlState.Trim()).Append('\n');
        text.Append("Error Message: ").Append(errorMessage).Append('\n');
        if (!string.IsNullOrWhiteSpace(detail)) text.Append("Detail: ").Append(detail.Trim()).Append('\n');
        if (!string.IsNullOrWhiteSpace(hint)) text.Append("Hint: ").Append(hint.Trim()).Append('\n');
        text.Append("\nFailing SQL:\n").Append(failingSql);
        return text.ToString();
    }
}
