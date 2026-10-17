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
    /// <param name="dialect">
    /// Spec 042 (FR-080): the SQL language of the active connection. <see cref="SqlDialect.SqlServer"/> (the default) gives
    /// the text that existed before PostgreSQL support, character for character.
    /// </param>
    /// <param name="serverVersion">
    /// The PostgreSQL server version, when known (for example <c>16.4</c>). Ignored for SQL Server, whose text is unchanged.
    /// </param>
    /// <returns>A fully constructed system prompt string.</returns>
    public static string BuildSystemPrompt(
        string schemaText,
        string taskInstruction,
        SqlDialect dialect = SqlDialect.SqlServer,
        string? serverVersion = null)
    {
        if (dialect == SqlDialect.PostgreSql)
            return BuildPostgreSqlSystemPrompt(schemaText, taskInstruction, serverVersion);

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

    /// <summary>
    /// Spec 042 (FR-080, FR-090): the PostgreSQL counterpart of the system prompt. It names PostgreSQL, forbids T-SQL and
    /// says nothing about SQL Server, so a PostgreSQL connection never receives an instruction written for the other dialect.
    /// </summary>
    private static string BuildPostgreSqlSystemPrompt(string schemaText, string taskInstruction, string? serverVersion)
    {
        var version = DescribePostgreSqlServer(serverVersion);

        return $"""
            You are an expert PostgreSQL developer. You work with PostgreSQL SQL and PL/pgSQL.{version}

            {taskInstruction}

            The user's database has the following schema:

            {schemaText}

            Important rules:
            - Use only tables and columns from the provided schema
            - Use PostgreSQL syntax. Do not use T-SQL: no TOP (use LIMIT), ISNULL (use COALESCE), GETDATE() (use now()), CONVERT (use CAST or ::), square-bracket quoting, @variables or GO
            - Use schema-qualified names (e.g., public.table_name)
            - Put an identifier in double quotes only when it needs them (mixed case, spaces or a reserved word)
            - Return only what is requested, no extra commentary unless asked
            """;
    }

    /// <summary>
    /// " The server is PostgreSQL 16.4." for a known version, otherwise nothing: a version is never invented. Only the first
    /// line of the text is used and it is cut short, because the value comes from the connection and goes into a prompt.
    /// </summary>
    private static string DescribePostgreSqlServer(string? serverVersion)
    {
        if (string.IsNullOrWhiteSpace(serverVersion)) return string.Empty;

        var line = serverVersion.Trim().Split('\n')[0].Trim();
        if (line.Length > 64) line = line[..64];
        if (line.Length == 0) return string.Empty;

        return line.StartsWith("PostgreSQL", StringComparison.OrdinalIgnoreCase)
            ? $" The server is {line}."
            : $" The server is PostgreSQL {line}.";
    }
}
