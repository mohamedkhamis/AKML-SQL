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

    /// <summary>
    /// Builds the system and user messages for an AI Fix prompt.
    /// </summary>
    /// <param name="schemaText">Formatted schema context from <see cref="Context.SchemaContextFormatter"/>.</param>
    /// <param name="failingSql">The SQL text that produced an error.</param>
    /// <param name="errorMessage">The error message returned by SQL Server.</param>
    /// <param name="errorNumber">The SQL Server error number (e.g. 208 for invalid object name).</param>
    /// <returns>A tuple of (systemMessage, userMessage).</returns>
    public static (string systemMessage, string userMessage) Build(
        string schemaText,
        string failingSql,
        string errorMessage,
        int errorNumber)
    {
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
}
