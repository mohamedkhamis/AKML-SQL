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

    /// <summary>
    /// Builds the full system message for the chat panel, including schema context.
    /// </summary>
    /// <param name="schemaText">Formatted schema context from <see cref="Context.SchemaContextFormatter"/>.</param>
    /// <returns>The complete system prompt string.</returns>
    public static string Build(string schemaText)
    {
        return PromptTemplates.BuildSystemPrompt(schemaText, TaskInstruction);
    }
}
