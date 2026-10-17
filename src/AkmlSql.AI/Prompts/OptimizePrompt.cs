namespace AkmlSql.Engine.Ai.Prompts;

/// <summary>
/// Builds system and user messages for AI-powered SQL optimization (Phase 9, US4).
/// The system prompt instructs the AI to analyze the query for performance improvements,
/// returning the optimized SQL, explanations, annotations, and optional index suggestions.
/// </summary>
public static class OptimizePrompt
{
    private const string TaskInstruction = """
        Analyze the following SQL query for performance improvements. Consider:
        - SARGability (ensure WHERE clause predicates can use indexes efficiently)
        - JOIN ordering (place the most selective/smallest table first when possible)
        - Index utilization (restructure predicates to leverage existing indexes)
        - Unnecessary DISTINCT or subquery elimination
        - Cursor-to-set-based alternatives (replace cursors with set-based operations)
        - Avoid wrapping indexed columns in functions (e.g., use date range instead of YEAR())
        - Prefer EXISTS over IN for subqueries when appropriate
        - Reduce unnecessary columns in SELECT (especially in subqueries)

        Return your response in the following exact format with these section headers:

        OPTIMIZED SQL:
        (the improved T-SQL query)

        EXPLANATION:
        (what was changed and why, as a concise paragraph)

        ANNOTATIONS:
        (list each change as one line in the format below)
        [SAFE] Line N: Description of safe change
        [REVIEW] Line N-M: Description of change that needs manual review

        INDEX SUGGESTIONS:
        (CREATE INDEX scripts if applicable, one per line, with estimated improvement as a comment)
        CREATE INDEX [IX_Name] ON [schema].[table]([columns]) INCLUDE ([columns]) -- Improvement: X%, Size: YKB, Write Impact: Low|Medium|High

        If no optimizations are needed, return the original SQL under OPTIMIZED SQL and state "No optimizations needed" under EXPLANATION.
        If no index suggestions apply, write "None" under INDEX SUGGESTIONS.
        """;

    // Spec 042 (FR-080): same section headers and line formats (the readers of the answer depend on them), PostgreSQL wording.
    private const string PostgreSqlTaskInstruction = """
        Analyze the following SQL query for performance improvements. Consider:
        - SARGability (ensure WHERE clause predicates can use indexes efficiently; a function on an indexed column needs an expression index)
        - JOIN ordering and join conditions (let the planner use the most selective filter first)
        - Index utilization (restructure predicates to leverage existing indexes, including partial indexes)
        - Unnecessary DISTINCT or subquery elimination
        - Row-by-row loops or cursors (replace with set-based statements)
        - Avoid wrapping indexed columns in functions (e.g., use a date range instead of date_part())
        - Prefer EXISTS over IN for subqueries when appropriate
        - Common table expressions that PostgreSQL materializes when they are used more than once (MATERIALIZED / NOT MATERIALIZED)
        - Reduce unnecessary columns in SELECT (especially in subqueries)

        Return your response in the following exact format with these section headers:

        OPTIMIZED SQL:
        (the improved PostgreSQL query)

        EXPLANATION:
        (what was changed and why, as a concise paragraph)

        ANNOTATIONS:
        (list each change as one line in the format below)
        [SAFE] Line N: Description of safe change
        [REVIEW] Line N-M: Description of change that needs manual review

        INDEX SUGGESTIONS:
        (CREATE INDEX statements if applicable, one per line, with estimated improvement as a comment)
        CREATE INDEX ix_name ON schema.table (columns) INCLUDE (columns) -- Improvement: X%, Size: YKB, Write Impact: Low|Medium|High

        If no optimizations are needed, return the original SQL under OPTIMIZED SQL and state "No optimizations needed" under EXPLANATION.
        If no index suggestions apply, write "None" under INDEX SUGGESTIONS.
        """;

    /// <summary>
    /// Builds the system and user messages for an optimization prompt.
    /// </summary>
    /// <param name="schemaText">Formatted schema context (level 3, includes indexes).</param>
    /// <param name="selectedSql">The SQL text to optimize.</param>
    /// <param name="dialect">The SQL language of the active connection (spec 042). SQL Server by default.</param>
    /// <param name="serverVersion">The PostgreSQL server version when known; ignored for SQL Server.</param>
    /// <returns>A tuple of (systemMessage, userMessage).</returns>
    public static (string systemMessage, string userMessage) Build(
        string schemaText,
        string selectedSql,
        SqlDialect dialect = SqlDialect.SqlServer,
        string? serverVersion = null)
    {
        var system = PromptTemplates.BuildSystemPrompt(
            schemaText,
            dialect == SqlDialect.PostgreSql ? PostgreSqlTaskInstruction : TaskInstruction,
            dialect,
            serverVersion);
        return (system, selectedSql);
    }
}
