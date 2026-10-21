namespace AkmlSql.Engine.Ai.Prompts;

/// <summary>
/// Builds the system and user prompts for inline ghost-text completion (Phase 9, US7 / T055).
/// The system prompt instructs the AI to return ONLY the predicted continuation text,
/// with no explanations, code fences, or markdown.
/// </summary>
public static class GhostTextPrompt
{
    private const string TaskInstruction = """
        Complete the following SQL code. Return ONLY the predicted continuation text,
        nothing else. No explanations, no code fences, no markdown. Predict 1-5 lines maximum.
        """;

    /// <summary>
    /// Builds the system and user messages for a ghost-text completion request.
    /// </summary>
    /// <param name="schemaText">Formatted schema context (compact, level 1-2).</param>
    /// <param name="precedingText">The SQL text immediately before the cursor position.</param>
    /// <param name="dialect">The SQL language of the active connection (spec 042). SQL Server by default.</param>
    /// <param name="serverVersion">The PostgreSQL server version when known; ignored for SQL Server.</param>
    /// <returns>A tuple of (systemMessage, userMessage). The user message is the preceding text.</returns>
    public static (string systemMessage, string userMessage) Build(
        string schemaText,
        string precedingText,
        SqlDialect dialect = SqlDialect.SqlServer,
        string? serverVersion = null)
    {
        var system = PromptTemplates.BuildSystemPrompt(schemaText, TaskInstruction, dialect, serverVersion);
        return (system, precedingText);
    }
}
