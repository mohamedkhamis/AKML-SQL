namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// The kinds of statement whose command tag carries a row count. The driver reports one of these (or "other") for each
/// statement it ran; the tag itself is not exposed, so <see cref="PgCommandTag"/> composes it. Kept here, not taken from
/// the driver, so this library stays free of it.
/// </summary>
public enum PgStatementType
{
    Other,
    Select,
    Insert,
    Update,
    Delete,
    Merge,
    Copy,
    Fetch,
    Move,
    CreateTableAs,
}

/// <summary>
/// Spec 042 (FR-012, task T058) - the command tag psql prints after a statement: <c>SELECT 3</c>, <c>INSERT 0 2</c>,
/// <c>UPDATE 12</c>, <c>CREATE TABLE</c>, <c>COMMIT</c>. The server sends it, but the driver exposes only the statement's
/// kind and affected-row count, so the counted forms are built from those and every other command from its leading
/// words, which is what the server's own tag is for all of them.
/// </summary>
/// <param name="Tag">The tag text, empty for an empty statement.</param>
/// <param name="RowCount">The number the tag ends with, or -1 when the tag has none (a definition or control command).</param>
public readonly record struct PgCommandTag(string Tag, long RowCount)
{

    // Object kinds whose name is more than one word. Matching tries every entry and keeps the longest.
    private static readonly string[][] MultiWordKinds =
    {
        new[] { "MATERIALIZED", "VIEW" },
        new[] { "FOREIGN", "TABLE" },
        new[] { "FOREIGN", "DATA", "WRAPPER" },
        new[] { "EVENT", "TRIGGER" },
        new[] { "ACCESS", "METHOD" },
        new[] { "USER", "MAPPING" },
        new[] { "TEXT", "SEARCH", "CONFIGURATION" },
        new[] { "TEXT", "SEARCH", "DICTIONARY" },
        new[] { "TEXT", "SEARCH", "PARSER" },
        new[] { "TEXT", "SEARCH", "TEMPLATE" },
        new[] { "OPERATOR", "CLASS" },
        new[] { "OPERATOR", "FAMILY" },
        new[] { "DEFAULT", "PRIVILEGES" },
        new[] { "LARGE", "OBJECT" },
    };

    // Words that may sit between CREATE and the object kind without being the kind.
    private static readonly HashSet<string> CreateModifiers = new(StringComparer.Ordinal)
    {
        "OR", "REPLACE", "TEMP", "TEMPORARY", "UNLOGGED", "GLOBAL", "LOCAL", "UNIQUE", "RECURSIVE", "TRUSTED",
        "PROCEDURAL", "CONSTRAINT",
    };

    /// <summary>The tag for a statement the driver reported as <paramref name="type"/> with <paramref name="rows"/> rows.</summary>
    public static PgCommandTag For(string sql, PgStatementType type, long rows) => For(PgLeadingWords.Of(sql), type, rows);

    /// <summary>The same for a statement of a split script, which knows its leading words already.</summary>
    public static PgCommandTag For(PgStatement statement, PgStatementType type, long rows) => For(statement.LeadingWords, type, rows);

    private static PgCommandTag For(IReadOnlyList<string> leadingWords, PgStatementType type, long rows)
    {
        // A server that reports no count sends none; never print a negative number.
        var count = rows < 0 ? 0 : rows;

        switch (type)
        {
            case PgStatementType.Select:
            case PgStatementType.CreateTableAs:
                return new PgCommandTag("SELECT " + count, count);
            case PgStatementType.Insert:
                return new PgCommandTag("INSERT 0 " + count, count);
            case PgStatementType.Update:
                return new PgCommandTag("UPDATE " + count, count);
            case PgStatementType.Delete:
                return new PgCommandTag("DELETE " + count, count);
            case PgStatementType.Merge:
                return new PgCommandTag("MERGE " + count, count);
            case PgStatementType.Copy:
                return new PgCommandTag("COPY " + count, count);
            case PgStatementType.Fetch:
                return new PgCommandTag("FETCH " + count, count);
            case PgStatementType.Move:
                return new PgCommandTag("MOVE " + count, count);
            default:
                return new PgCommandTag(FromLeadingWords(leadingWords), -1);
        }
    }

    /// <summary>Maps the driver's statement-type name; a name this library does not count rows for is <see cref="PgStatementType.Other"/>.</summary>
    public static PgStatementType TypeFromDriverName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return PgStatementType.Other;

        // SELECT ... INTO creates a table and still reports itself as SELECT n.
        if (string.Equals(name, "SelectInto", StringComparison.OrdinalIgnoreCase)) return PgStatementType.Select;

        // Only names: a numeric string must not parse to a value.
        if (char.IsDigit(name[0]) || name[0] == '-') return PgStatementType.Other;
        return Enum.TryParse<PgStatementType>(name, ignoreCase: true, out var type) && Enum.IsDefined(type)
            ? type
            : PgStatementType.Other;
    }

    private static string FromLeadingWords(IReadOnlyList<string> words)
    {
        if (words.Count == 0) return string.Empty;

        switch (words[0])
        {
            case "CREATE":
            case "ALTER":
            case "DROP":
                var kind = ObjectKind(words, 1, skipCreateModifiers: words[0] == "CREATE");
                return kind.Length == 0 ? words[0] : words[0] + " " + kind;

            case "START": return words.Count > 1 && words[1] == "TRANSACTION" ? "START TRANSACTION" : "START";
            case "TRUNCATE": return "TRUNCATE TABLE";
            case "LOCK": return "LOCK TABLE";
            case "END": return "COMMIT";
            case "ABORT": return "ROLLBACK";
            case "ANALYSE": return "ANALYZE";
            case "DECLARE": return "DECLARE CURSOR";
            case "CLOSE": return words.Count > 1 && words[1] == "ALL" ? "CLOSE CURSOR ALL" : "CLOSE CURSOR";

            case "REFRESH":
                return words.Count > 2 && words[1] == "MATERIALIZED" && words[2] == "VIEW" ? "REFRESH MATERIALIZED VIEW" : "REFRESH";

            case "COMMIT":
            case "ROLLBACK":
                return words.Count > 1 && words[1] == "PREPARED" ? words[0] + " PREPARED" : words[0];

            case "PREPARE":
                return words.Count > 1 && words[1] == "TRANSACTION" ? "PREPARE TRANSACTION" : "PREPARE";

            case "SET":
                return words.Count > 1 && words[1] == "CONSTRAINTS" ? "SET CONSTRAINTS" : "SET";

            case "DEALLOCATE":
                return words.Count > 1 && words[1] == "ALL" ? "DEALLOCATE ALL" : "DEALLOCATE";

            case "DISCARD":
                if (words.Count < 2) return "DISCARD";
                return words[1] == "TEMPORARY" ? "DISCARD TEMP" : "DISCARD " + words[1];

            default:
                return words[0];
        }
    }

    // The object kind after CREATE / ALTER / DROP: one word, or one of the multi-word forms.
    private static string ObjectKind(IReadOnlyList<string> words, int from, bool skipCreateModifiers)
    {
        var i = from;
        if (skipCreateModifiers)
        {
            while (i < words.Count && CreateModifiers.Contains(words[i])) i++;
        }
        if (i >= words.Count) return string.Empty;

        string[]? best = null;
        foreach (var form in MultiWordKinds)
        {
            if (i + form.Length > words.Count) continue;
            var match = true;
            for (var k = 0; k < form.Length; k++)
            {
                if (words[i + k] != form[k]) { match = false; break; }
            }
            if (match && (best == null || form.Length > best.Length)) best = form;
        }
        if (best != null) return string.Join(" ", best);

        // USER and GROUP are other spellings of ROLE; the server reports ROLE.
        return words[i] is "USER" or "GROUP" ? "ROLE" : words[i];
    }

}
