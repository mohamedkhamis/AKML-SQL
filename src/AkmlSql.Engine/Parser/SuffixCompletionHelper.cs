namespace AkmlSql.Engine.Parser;

public static class SuffixCompletionHelper
{
    private const string DummyIdentifier = "__akml_dummy__";

    /// Append dummy tokens to incomplete SQL to produce a valid AST
    public static string AppendDummyTokens(string sql)
    {
        var trimmed = sql.TrimEnd();
        if (string.IsNullOrEmpty(trimmed))
        {
            return $"SELECT {DummyIdentifier}";
        }

        var upper = trimmed.ToUpperInvariant();

        // After SELECT — needs column list
        if (upper.EndsWith("SELECT"))
        {
            return trimmed + $" {DummyIdentifier}";
        }

        // After FROM/JOIN — needs table reference
        if (upper.EndsWith("FROM") || upper.EndsWith("JOIN") ||
            upper.EndsWith("INNER JOIN") || upper.EndsWith("LEFT JOIN") ||
            upper.EndsWith("RIGHT JOIN") || upper.EndsWith("CROSS JOIN") ||
            upper.EndsWith("LEFT OUTER JOIN") || upper.EndsWith("RIGHT OUTER JOIN") ||
            upper.EndsWith("FULL OUTER JOIN") || upper.EndsWith("FULL JOIN"))
        {
            return trimmed + $" {DummyIdentifier}";
        }

        // After WHERE/AND/OR — needs expression
        if (upper.EndsWith("WHERE") || upper.EndsWith("AND") || upper.EndsWith("OR"))
        {
            return trimmed + $" {DummyIdentifier} = 1";
        }

        // After SET — needs column = value
        if (upper.EndsWith("SET"))
        {
            return trimmed + $" {DummyIdentifier} = 1";
        }

        // After dot — needs identifier
        if (trimmed.EndsWith("."))
        {
            return trimmed + DummyIdentifier;
        }

        // After EXEC/EXECUTE — needs proc name
        if (upper.EndsWith("EXEC") || upper.EndsWith("EXECUTE"))
        {
            return trimmed + $" {DummyIdentifier}";
        }

        // After ORDER BY / GROUP BY — needs column
        if (upper.EndsWith("ORDER BY") || upper.EndsWith("GROUP BY"))
        {
            return trimmed + $" {DummyIdentifier}";
        }

        // Default: append dummy identifier
        return trimmed + $" {DummyIdentifier}";
    }

    public static bool IsDummyIdentifier(string name)
    {
        return name.Equals(DummyIdentifier, StringComparison.OrdinalIgnoreCase);
    }
}
