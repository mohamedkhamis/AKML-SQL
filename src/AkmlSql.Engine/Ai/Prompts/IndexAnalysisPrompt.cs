namespace AkmlSql.Engine.Ai.Prompts;

/// <summary>
/// Builds system and user messages for AI-powered index analysis (Phase 9, US5).
/// The system prompt instructs the AI to analyze query patterns and existing indexes
/// to suggest missing index opportunities with CREATE INDEX scripts and impact estimates.
/// </summary>
public static class IndexAnalysisPrompt
{
    private const string TaskInstruction = """
        Analyze the following SQL query for missing index opportunities. Consider:
        - The query pattern (WHERE clauses, JOIN conditions, ORDER BY, GROUP BY)
        - Existing indexes on the referenced tables (provided in the schema context)
        - Table structures and column selectivity
        - Whether a covering index with INCLUDE columns would eliminate key lookups

        Return your response in the following exact format:

        SUMMARY:
        (a concise paragraph summarizing the index analysis findings)

        INDEX SUGGESTIONS:
        (one CREATE INDEX statement per suggestion, each on its own line with metadata as a trailing comment)
        CREATE INDEX [IX_Name] ON [schema].[table]([key_columns]) INCLUDE ([include_columns]) -- Improvement: X%, Size: YKB, Write Impact: Low|Medium|High

        Rules for index suggestions:
        - Use descriptive index names prefixed with IX_
        - Key columns should be ordered by selectivity (most selective first)
        - Include INCLUDE columns only when they would make the index covering for the query
        - Estimated improvement should reflect the expected read performance gain
        - Estimated size should be a rough KB estimate based on table row count and column widths
        - Write Impact should be Low (narrow index, few columns), Medium (moderate), or High (wide index, many columns, or on a heavily written table)
        - Do not suggest indexes that already exist in the schema context
        - If no index improvements are needed, write "None — existing indexes are sufficient for this query"
        """;

    /// <summary>
    /// Builds the system and user messages for an index analysis prompt.
    /// </summary>
    /// <param name="schemaText">Formatted schema context (level 3, includes existing indexes).</param>
    /// <param name="selectedSql">The SQL text to analyze for indexing opportunities.</param>
    /// <param name="executionPlanXml">
    /// Optional XML execution plan. When available, provides concrete cost data and
    /// missing index DMV suggestions to improve the AI analysis quality.
    /// </param>
    /// <returns>A tuple of (systemMessage, userMessage).</returns>
    public static (string systemMessage, string userMessage) Build(
        string schemaText,
        string selectedSql,
        string? executionPlanXml)
    {
        var system = PromptTemplates.BuildSystemPrompt(schemaText, TaskInstruction);

        var userMessage = selectedSql;
        if (!string.IsNullOrWhiteSpace(executionPlanXml))
        {
            userMessage = $"""
                SQL Query:
                {selectedSql}

                Execution Plan XML:
                {executionPlanXml}
                """;
        }

        return (system, userMessage);
    }
}
