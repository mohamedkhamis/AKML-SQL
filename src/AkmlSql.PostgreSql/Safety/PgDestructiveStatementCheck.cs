using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Safety;
using AkmlSql.PostgreSql.Completion;
using AkmlSql.PostgreSql.Language;

namespace AkmlSql.PostgreSql.Safety;

/// <summary>
/// Spec 042 (task T105, FR-069) - the confirmation before a PostgreSQL script runs: <c>DROP</c>, <c>TRUNCATE</c>, and
/// <c>DELETE</c> or <c>UPDATE</c> with no <c>WHERE</c>. It reads the script with the managed scanner, so dollar-quoted function
/// bodies and strings are not statements, and a statement that does not parse is still read. It never blocks: a script it cannot
/// make sense of simply has no warnings.
/// </summary>
public static class PgDestructiveStatementCheck
{
    /// <param name="server">Named in the production warnings.</param>
    /// <param name="isProduction">The connection is marked as a production server.</param>
    public static SafetyWarningDto[] Analyze(string? sql, string? server = null, bool isProduction = false)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Array.Empty<SafetyWarningDto>();

        var warnings = new List<SafetyWarningDto>();
        var hasDml = false;
        var hasDdl = false;

        foreach (var statement in PgStatementSplitter.Split(sql!))
        {
            if (statement.IsMetaCommand) continue;
            var tokens = PgLexicalScanner.Scan(statement.Text);
            var sig = PgStatementTokens.Build(statement.Text, tokens, 0, statement.Text.Length);
            if (sig.Count == 0) continue;

            Classify(sig, ref hasDml, ref hasDdl);
            DataStatements(sig, warnings);
            Definitions(sig, warnings);
        }

        if (isProduction)
        {
            if (hasDml)
                warnings.Add(new SafetyWarningDto
                {
                    WarningType = (int)SafetyWarningType.ProductionDml,
                    Message = $"You are about to execute DML statements on production server '{server}'. Proceed with caution.",
                    Severity = 1,
                });
            if (hasDdl)
                warnings.Add(new SafetyWarningDto
                {
                    WarningType = (int)SafetyWarningType.ProductionDdl,
                    Message = $"You are about to execute DDL statements on production server '{server}'. This may affect the database schema.",
                    Severity = 2,
                });
        }
        return warnings.ToArray();
    }

    private static void Classify(PgStatementTokens t, ref bool hasDml, ref bool hasDdl)
    {
        var first = FirstVerb(t);
        if (first is "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "TRUNCATE" or "COPY") hasDml = true;
        if (first is "CREATE" or "ALTER" or "DROP" or "TRUNCATE" or "GRANT" or "REVOKE" or "COMMENT" or "REINDEX" or "VACUUM" or "CLUSTER") hasDdl = true;
        // a data-modifying CTE
        if (first == "WITH")
            for (var i = 0; i < t.Count; i++)
                if (t[i].IsWord && t[i].Upper is "INSERT" or "UPDATE" or "DELETE") hasDml = true;
    }

    // the command word, past EXPLAIN [ANALYZE] ...
    private static string FirstVerb(PgStatementTokens t)
    {
        var i = 0;
        while (i < t.Count && t[i].IsWord && t[i].Upper is "EXPLAIN" or "ANALYZE" or "VERBOSE") i++;
        return i < t.Count && t[i].IsWord ? t[i].Upper : string.Empty;
    }

    // DELETE FROM t ... / UPDATE t SET ... without WHERE, at any nesting (a data-modifying CTE counts)
    private static void DataStatements(PgStatementTokens t, List<SafetyWarningDto> warnings)
    {
        for (var i = 0; i < t.Count; i++)
        {
            var w = t[i];
            if (!w.IsWord) continue;

            if (w.Upper == "DELETE" && i + 1 < t.Count && t[i + 1].IsWordEqual("FROM") && !(i > 0 && t[i - 1].IsWordEqual("THEN")))
            {
                if (!HasWhere(t, i)) warnings.Add(Without(SafetyWarningType.DeleteWithoutWhere, "DELETE", Target(t, i + 2)));
            }
            else if (w.Upper == "UPDATE" && !(i > 0 && t[i - 1].IsWord && t[i - 1].Upper is "DO" or "FOR" or "ON" or "THEN" or "NO" or "KEY"))
            {
                if (i + 1 < t.Count && !HasWhere(t, i)) warnings.Add(Without(SafetyWarningType.UpdateWithoutWhere, "UPDATE", Target(t, i + 1)));
            }
        }
    }

    // a WHERE of the same query level before it ends (the closing parenthesis, the next statement, RETURNING does not end it)
    private static bool HasWhere(PgStatementTokens t, int start)
    {
        var depth = t[start].Depth;
        for (var j = start + 1; j < t.Count; j++)
        {
            if (t[j].Depth < depth) return false;
            if (t[j].Depth == depth && t[j].IsWordEqual("WHERE")) return true;
            // `WHERE CURRENT OF cursor` is a WHERE too
        }
        return false;
    }

    private static string? Target(PgStatementTokens t, int i)
    {
        while (i < t.Count && t[i].IsWord && t[i].Upper is "ONLY") i++;
        if (i >= t.Count || !t[i].IsName) return null;
        var name = t[i].Name.Text;
        while (i + 2 < t.Count && t[i + 1].IsDot && t[i + 2].IsName)
        {
            name += "." + t[i + 2].Name.Text;
            i += 2;
        }
        return name;
    }

    private static SafetyWarningDto Without(SafetyWarningType type, string verb, string? table) => new()
    {
        WarningType = (int)type,
        Message = $"{verb} without a WHERE clause will affect ALL rows{(table != null ? $" in '{table}'" : string.Empty)}.",
        ObjectName = table,
        Severity = 2,
    };

    // DROP ... and TRUNCATE ...
    private static void Definitions(PgStatementTokens t, List<SafetyWarningDto> warnings)
    {
        var first = FirstVerb(t);
        if (first == "TRUNCATE")
        {
            var i = 1;
            while (i < t.Count && t[i].IsWord && t[i].Upper is "TABLE" or "ONLY") i++;
            var table = Target(t, i);
            warnings.Add(new SafetyWarningDto
            {
                WarningType = (int)SafetyWarningType.TruncateTable,
                Message = $"TRUNCATE removes ALL rows{(table != null ? $" from '{table}'" : string.Empty)} and cannot be filtered.",
                ObjectName = table,
                Severity = 2,
            });
            return;
        }

        if (first != "DROP" || t.Count < 2) return;

        var kind = t[1].IsWord ? t[1].Upper : string.Empty;
        var at = 2;
        if (kind == "MATERIALIZED" && t.Count > 2) { kind = "MATERIALIZED VIEW"; at = 3; }
        if (at < t.Count && t[at].IsWordEqual("IF")) at += 2;          // IF EXISTS
        var name = Target(t, at);

        if (kind == "DATABASE")
            warnings.Add(new SafetyWarningDto { WarningType = (int)SafetyWarningType.DropDatabase, Message = $"DROP DATABASE will permanently delete the database{(name != null ? $" '{name}'" : string.Empty)} and everything in it.", ObjectName = name, Severity = 2 });
        else if (kind is "TABLE" or "SCHEMA" or "TABLESPACE" or "OWNED")
            warnings.Add(new SafetyWarningDto { WarningType = (int)SafetyWarningType.DropTable, Message = $"DROP {kind} will permanently remove {(kind == "TABLE" ? "the table" : "the " + kind.ToLowerInvariant())}{(name != null ? $" '{name}'" : string.Empty)} and its data.", ObjectName = name, Severity = 2 });
        else if (kind.Length > 0)
            warnings.Add(new SafetyWarningDto { WarningType = (int)SafetyWarningType.DropTable, Message = $"DROP {kind} will permanently remove {(name != null ? $"'{name}'" : "the object")}.", ObjectName = name, Severity = 1 });
    }
}
