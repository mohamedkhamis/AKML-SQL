using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AkmlSql.Engine.Completion;

/// <summary>
/// When a T-SQL identifier needs square brackets, and how to add them.
/// <para>
/// This rule used to live privately inside <c>ObjectProvider</c>, which is why only the engine's
/// completion path applied it. The web edition's OFFLINE fallback — the one that runs when the
/// engine is unreachable and builds completions from the browser's cached schema — could not reach
/// it, so it inserted raw names: picking <c>Order Details</c> produced <c>Order Details</c>, which is
/// not valid T-SQL. The two paths are meant to offer the same completions, and a rule that exists
/// once cannot disagree with itself.
/// </para>
/// </summary>
public static class SqlIdentifier
{
    /// <summary>
    /// Regular-identifier pattern per T-SQL rules: first char a letter, <c>_</c>, <c>@</c>, or
    /// <c>#</c>; subsequent chars letters, decimal digits, <c>_</c>, <c>@</c>, <c>#</c>, or <c>$</c>.
    /// "Letter" and "digit" are Unicode's, as in SQL Server: <c>Übersicht</c>, <c>Заказы</c> and
    /// <c>العملاء</c> are regular identifiers. An ASCII-only pattern bracketed them needlessly,
    /// dropped every alias suggestion for them, and sent JOIN alias generation into an endless
    /// loop (no numeric suffix could make "ü" match).
    /// </summary>
    private static readonly Regex RegularIdentifier =
        new(@"^[\p{L}_@#][\p{L}\p{Nd}_@#$]*$", RegexOptions.Compiled);

    /// <summary>
    /// True when <paramref name="name"/> must be bracketed to be valid: it is NOT a regular
    /// identifier (contains a space or hyphen, starts with a digit, or has other special
    /// characters), OR it is a T-SQL reserved word.
    /// </summary>
    public static bool NeedsQuoting(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!RegularIdentifier.IsMatch(name)) return true;
        return ReservedWords.Contains(name);
    }

    /// <summary>
    /// QUOTENAME semantics: wrap <paramref name="part"/> in brackets, doubling any embedded
    /// <c>']'</c> so the result is a valid delimited identifier. Always brackets.
    /// </summary>
    public static string Quote(string part) => "[" + part.Replace("]", "]]") + "]";

    /// <summary>
    /// Brackets one identifier part only when it needs it; leaves an already-bracketed part alone.
    /// <c>Order Details</c> → <c>[Order Details]</c>; <c>Orders</c> → <c>Orders</c>;
    /// <c>Order</c> → <c>[Order]</c> (reserved word).
    /// </summary>
    public static string QuoteIfNeeded(string? part)
    {
        if (string.IsNullOrEmpty(part)) return part ?? string.Empty;
        if (IsAlreadyQuoted(part)) return part;
        return NeedsQuoting(part) ? Quote(part) : part;
    }

    /// <summary>
    /// Brackets each part of a multi-part name independently, only where needed:
    /// (<c>dbo</c>, <c>Order Details</c>) → <c>dbo.[Order Details]</c>.
    /// </summary>
    /// <remarks>
    /// Takes the parts separately rather than a dotted string on purpose: a name can legitimately
    /// contain a dot, and splitting <c>"Sales.2024"</c> on it would quote the wrong thing.
    /// </remarks>
    public static string QuoteIfNeeded(params string[] parts)
    {
        if (parts is null || parts.Length == 0) return string.Empty;

        var quoted = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            quoted[i] = QuoteIfNeeded(parts[i]);

        return string.Join(".", quoted);
    }

    /// <summary>
    /// Brackets one identifier part the way the IntelliSense option
    /// <c>IntelliSense.Qualification.BracketMode</c> asks: Always brackets every name,
    /// WhenRequired (the default) only the ones that need it, Never none (an existing pair is
    /// removed). This is the only implementation of the option: table completions
    /// (ObjectProvider) and column, JOIN and ON completions all call it, so one completion list
    /// never mixes two bracket policies.
    /// </summary>
    public static string Apply(string? part, AkmlSql.Core.Config.BracketMode mode)
    {
        if (string.IsNullOrEmpty(part)) return part ?? string.Empty;
        return mode switch
        {
            AkmlSql.Core.Config.BracketMode.Always => IsAlreadyQuoted(part) ? part : Quote(part),
            AkmlSql.Core.Config.BracketMode.Never => IsAlreadyQuoted(part) ? part.Substring(1, part.Length - 2) : part,
            _ => QuoteIfNeeded(part),
        };
    }

    /// <summary>Each part per <paramref name="mode"/>, joined with dots (see <see cref="QuoteIfNeeded(string[])"/>).</summary>
    public static string Apply(AkmlSql.Core.Config.BracketMode mode, params string[] parts)
    {
        if (parts is null || parts.Length == 0) return string.Empty;
        var result = new string[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            result[i] = Apply(parts[i], mode);
        return string.Join(".", result);
    }

    private static bool IsAlreadyQuoted(string part) =>
        part.Length >= 2 &&
        part.StartsWith("[", StringComparison.Ordinal) &&
        part.EndsWith("]", StringComparison.Ordinal);

    /// <summary>
    /// T-SQL reserved keywords that cannot be used as an unquoted identifier.
    /// (ISO/Transact-SQL reserved word list — not the full keyword set.)
    /// </summary>
    private static readonly HashSet<string> ReservedWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION",
            "BACKUP", "BEGIN", "BETWEEN", "BREAK", "BROWSE", "BULK", "BY",
            "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED",
            "COALESCE", "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT",
            "CONTAINS", "CONTAINSTABLE", "CONTINUE", "CONVERT", "CREATE", "CROSS",
            "CURRENT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
            "CURRENT_USER", "CURSOR", "DATABASE", "DBCC", "DEALLOCATE", "DECLARE",
            "DEFAULT", "DELETE", "DENY", "DESC", "DISK", "DISTINCT", "DISTRIBUTED",
            "DOUBLE", "DROP", "DUMP", "ELSE", "END", "ERRLVL", "ESCAPE", "EXCEPT",
            "EXEC", "EXECUTE", "EXISTS", "EXIT", "EXTERNAL", "FETCH", "FILE",
            "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE", "FROM",
            "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", "HOLDLOCK",
            "IDENTITY", "IDENTITY_INSERT", "IDENTITYCOL", "IF", "IN", "INDEX",
            "INNER", "INSERT", "INTERSECT", "INTO", "IS", "JOIN", "KEY", "KILL",
            "LEFT", "LIKE", "LINENO", "LOAD", "MERGE", "NATIONAL", "NOCHECK",
            "NONCLUSTERED", "NOT", "NULL", "NULLIF", "OF", "OFF", "OFFSETS", "ON",
            "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML",
            "OPTION", "OR", "ORDER", "OUTER", "OVER", "PERCENT", "PIVOT", "PLAN",
            "PRECISION", "PRIMARY", "PRINT", "PROC", "PROCEDURE", "PUBLIC",
            "RAISERROR", "READ", "READTEXT", "RECONFIGURE", "REFERENCES",
            "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVERT", "REVOKE",
            "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE", "SAVE",
            "SCHEMA", "SECURITYAUDIT", "SELECT", "SEMANTICKEYPHRASETABLE",
            "SEMANTICSIMILARITYDETAILSTABLE", "SEMANTICSIMILARITYTABLE",
            "SESSION_USER", "SET", "SETUSER", "SHUTDOWN", "SOME", "STATISTICS",
            "SYSTEM_USER", "TABLE", "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP",
            "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE", "TRY_CONVERT", "TSEQUAL",
            "UNION", "UNIQUE", "UNPIVOT", "UPDATE", "UPDATETEXT", "USE", "USER",
            "VALUES", "VARYING", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE",
            "WITH", "WITHIN", "WRITETEXT"
        };
}
