namespace AkmlSql.Engine.Ai.Prompts;

/// <summary>
/// Shared prompt building logic for all AI assistance features.
/// Constructs system prompts with schema context and task-specific instructions.
/// </summary>
public static class PromptTemplates
{
    /// <summary>
    /// Builds a system prompt combining a task instruction and schema context.
    /// </summary>
    /// <param name="schemaText">Formatted schema context from <see cref="Context.SchemaContextFormatter"/>.</param>
    /// <param name="taskInstruction">Task-specific instruction (e.g., generate SQL, explain SQL).</param>
    /// <returns>A fully constructed system prompt string.</returns>
    public static string BuildSystemPrompt(string schemaText, string taskInstruction)
    {
        return $"""
            You are an expert SQL Server developer. You work with T-SQL (Microsoft SQL Server dialect).

            {taskInstruction}

            The user's database has the following schema:

            {schemaText}

            Important rules:
            - Use only tables and columns from the provided schema
            - Use T-SQL syntax (not MySQL, PostgreSQL, or other dialects)
            - Use schema-qualified names (e.g., dbo.TableName)
            - Return only what is requested, no extra commentary unless asked
            """;
    }
}
