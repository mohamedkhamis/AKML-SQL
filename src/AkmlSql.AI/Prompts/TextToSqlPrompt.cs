namespace AkmlSql.Engine.Ai.Prompts;

/// <summary>
/// Builds system and user messages for Text-to-SQL generation (Phase 9, US1).
/// The system prompt instructs the AI to generate a single T-SQL query from a
/// natural-language description, using only schema objects provided in the context.
/// </summary>
public static class TextToSqlPrompt
{
    private const string TaskInstruction = """
        Generate a single SQL query for the user's request.
        Return ONLY the SQL query with no explanations, markdown formatting, or code fences.
        If the request is ambiguous, make reasonable assumptions and generate the most likely intended query.
        """;

    /// <summary>
    /// Builds the system and user messages for a Text-to-SQL prompt.
    /// </summary>
    /// <param name="schemaText">Formatted schema context.</param>
    /// <param name="userPrompt">The user's natural-language description of the desired query.</param>
    /// <returns>A tuple of (systemMessage, userMessage).</returns>
    public static (string systemMessage, string userMessage) Build(string schemaText, string userPrompt)
    {
        var system = PromptTemplates.BuildSystemPrompt(schemaText, TaskInstruction);
        return (system, userPrompt);
    }
}
