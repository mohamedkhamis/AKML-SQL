namespace AkmlSql.Engine.Ai.Prompts;

/// <summary>
/// Builds system and user messages for AI-powered SQL explanation (Phase 9, US2).
/// The system prompt instructs the AI to break down the SQL into structured sections:
/// PURPOSE, STEP BY STEP, KEY DETAILS, and SUGGESTIONS.
/// </summary>
public static class ExplainPrompt
{
    private const string TaskInstruction = """
        Explain the following SQL code. Structure your response with these exact sections:
        PURPOSE: (one sentence summary)
        STEP BY STEP: (numbered list)
        KEY DETAILS: (data types, performance, edge cases)
        SUGGESTIONS: (optional improvements)

        Use only the section headers shown above. Do not use markdown formatting.
        Each section header must appear on its own line followed by the content.
        If a section has no relevant content, write "None" after the header.
        """;

    /// <summary>
    /// Builds the system and user messages for an AI Explain prompt.
    /// </summary>
    /// <param name="schemaText">Formatted schema context from <see cref="Context.SchemaContextFormatter"/>.</param>
    /// <param name="selectedSql">The SQL text to explain.</param>
    /// <returns>A tuple of (systemMessage, userMessage).</returns>
    public static (string systemMessage, string userMessage) Build(string schemaText, string selectedSql)
    {
        var system = PromptTemplates.BuildSystemPrompt(schemaText, TaskInstruction);
        var user = $"Explain this SQL:\n\n{selectedSql}";
        return (system, user);
    }
}
