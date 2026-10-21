namespace AkmlSql.PostgreSql.Formatting;

/// <summary>
/// Word lists the formatter needs: which words PostgreSQL reserves (Appendix C of the manual), the words that never take a
/// function-call parenthesis, the clause words of a query, and the multi-word data types. Plain data, no behaviour.
/// </summary>
internal static class PgWords
{
    /// <summary>Reserved words, and the words reserved except as function or type names: they can never be an unquoted name here.</summary>
    public static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "ALL", "ANALYSE", "ANALYZE", "AND", "ANY", "ARRAY", "AS", "ASC", "ASYMMETRIC", "BOTH", "CASE", "CHECK", "COLLATE", "COLUMN",
        "CONSTRAINT", "CREATE", "CURRENT_CATALOG", "CURRENT_DATE", "CURRENT_ROLE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER",
        "DEFAULT", "DEFERRABLE", "DESC", "DISTINCT", "DO", "ELSE", "END", "EXCEPT", "FALSE", "FETCH", "FOR", "FOREIGN", "FROM", "GRANT",
        "GROUP", "HAVING", "IN", "INITIALLY", "INTERSECT", "INTO", "LATERAL", "LEADING", "LIMIT", "LOCALTIME", "LOCALTIMESTAMP", "NOT",
        "NULL", "OFFSET", "ON", "ONLY", "OR", "ORDER", "PLACING", "PRIMARY", "REFERENCES", "RETURNING", "SELECT", "SESSION_USER", "SOME",
        "SYMMETRIC", "SYSTEM_USER", "TABLE", "THEN", "TO", "TRAILING", "TRUE", "UNION", "UNIQUE", "USER", "USING", "VARIADIC", "WHEN",
        "WHERE", "WINDOW", "WITH",
        // reserved except as a function or type name
        "AUTHORIZATION", "BINARY", "COLLATION", "CONCURRENTLY", "CROSS", "CURRENT_SCHEMA", "FREEZE", "FULL", "ILIKE", "INNER", "IS",
        "ISNULL", "JOIN", "LEFT", "LIKE", "NATURAL", "NOTNULL", "OUTER", "OVERLAPS", "RIGHT", "SIMILAR", "TABLESAMPLE", "VERBOSE",
    };

    /// <summary>Words that are a value, not an operator or a clause: an operand can end with one.</summary>
    public static readonly HashSet<string> ValueWords = new(StringComparer.Ordinal)
    {
        "NULL", "TRUE", "FALSE", "CURRENT_CATALOG", "CURRENT_DATE", "CURRENT_ROLE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER",
        "LOCALTIME", "LOCALTIMESTAMP", "SESSION_USER", "SYSTEM_USER", "USER", "CURRENT_SCHEMA", "END", "DEFAULT",
    };

    /// <summary>
    /// Words after which an opening parenthesis is NOT a function call's: the formatter puts a space before it. A reserved word is
    /// one (<c>IN (</c>, <c>AND (</c>, <c>FROM (</c>), except <c>ARRAY(</c>; the others are unreserved words that are syntax.
    /// </summary>
    public static readonly HashSet<string> SpaceBeforeParen = BuildSpaceBeforeParen();

    private static HashSet<string> BuildSpaceBeforeParen()
    {
        var set = new HashSet<string>(Reserved, StringComparer.Ordinal);
        set.Remove("ARRAY");
        set.Remove("LEFT");     // left(s, 2) is a call; LEFT JOIN is never followed by a parenthesis
        set.Remove("RIGHT");
        set.UnionWith(new[] { "EXISTS", "VALUES", "OVER", "FILTER", "NOT", "KEY", "SET", "AT", "WITHIN", "TABLE", "INDEX", "REPLACE" });
        return set;
    }

    /// <summary>Words that start (or are) a clause of a query block; used to find clause boundaries at the top level of a statement.</summary>
    public static readonly HashSet<string> SetOperators = new(StringComparer.Ordinal) { "UNION", "INTERSECT", "EXCEPT" };

    public static readonly HashSet<string> JoinStarters = new(StringComparer.Ordinal)
    {
        "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "NATURAL",
    };

    /// <summary>Keywords that are written like a function call (<c>CAST(x AS t)</c>, <c>COALESCE(a, b)</c>); the function-name casing applies.</summary>
    public static readonly HashSet<string> FunctionKeywords = new(StringComparer.Ordinal)
    {
        "CAST", "COALESCE", "EXTRACT", "GREATEST", "LEAST", "NULLIF", "OVERLAY", "POSITION", "SUBSTRING", "TRIM", "NORMALIZE",
        "XMLELEMENT", "XMLFOREST", "XMLCONCAT", "XMLPARSE", "XMLSERIALIZE", "XMLEXISTS", "XMLROOT", "XMLPI", "XMLATTRIBUTES",
        "JSON_OBJECT", "JSON_ARRAY", "JSON_VALUE", "JSON_QUERY", "JSON_EXISTS", "JSON_SERIALIZE", "JSON_SCALAR",
        "LEFT", "RIGHT", "GROUPING",
    };

    /// <summary>Words of a constraint or a column option that are syntax wherever they stand in a column definition after its name.</summary>
    public static readonly HashSet<string> ConstraintWords = new(StringComparer.Ordinal)
    {
        "CONSTRAINT", "KEY", "ALWAYS", "IDENTITY", "STORED", "GENERATED", "BY", "ON", "DELETE", "UPDATE", "CASCADE", "RESTRICT", "SET",
        "NO", "ACTION", "MATCH", "FULL", "SIMPLE", "PARTIAL", "DEFERRABLE", "INITIALLY", "DEFERRED", "IMMEDIATE", "COLLATE", "INCLUDE",
        "WITH", "NULLS", "DISTINCT", "USING", "INDEX", "TABLESPACE", "EXCLUDE", "LIKE", "INCLUDING", "EXCLUDING", "FOREIGN", "NOT",
        "NULL", "DEFAULT", "PRIMARY", "UNIQUE", "CHECK", "REFERENCES", "AS", "VIRTUAL", "INHERIT", "STORAGE", "COMPRESSION",
    };

    /// <summary>Words that make up the head of a CREATE / ALTER / DROP statement, before the name of the object.</summary>
    public static readonly HashSet<string> DdlHeadWords = new(StringComparer.Ordinal)
    {
        "CREATE", "ALTER", "DROP", "OR", "REPLACE", "TEMP", "TEMPORARY", "UNLOGGED", "GLOBAL", "LOCAL", "UNIQUE", "TABLE", "INDEX", "VIEW",
        "MATERIALIZED", "RECURSIVE", "SEQUENCE", "SCHEMA", "DATABASE", "ROLE", "USER", "GROUP", "EXTENSION", "FUNCTION", "PROCEDURE",
        "ROUTINE", "TRIGGER", "TYPE", "DOMAIN", "POLICY", "RULE", "OPERATOR", "AGGREGATE", "COLLATION", "CONVERSION", "LANGUAGE",
        "PUBLICATION", "SUBSCRIPTION", "SERVER", "FOREIGN", "DATA", "WRAPPER", "TABLESPACE", "IF", "NOT", "EXISTS", "CONCURRENTLY",
        "ONLY", "STATISTICS", "EVENT", "CAST", "TRANSFORM", "ACCESS", "METHOD", "DEFAULT", "PRIVILEGES", "OWNED", "TEXT", "SEARCH",
        "CONFIGURATION", "DICTIONARY", "PARSER", "TEMPLATE", "LARGE", "OBJECT", "SYSTEM", "COMMENT", "ON", "TRUNCATE", "GRANT", "REVOKE",
        "COLUMN", "CONSTRAINT", "REFRESH", "REINDEX", "CLUSTER", "VACUUM", "ANALYZE", "LOCK", "PROCEDURAL", "ASSERTION", "CASCADE", "RESTRICT",
    };

    /// <summary>
    /// Words that are syntax in utility and definition statements (ALTER, GRANT, MERGE, function options ...), cased as keywords there when
    /// they stand outside parentheses and are not part of a qualified name. Words that are often column or table names (TYPE, DATA, NAME,
    /// VALUE, STATUS, ROWS ...) are deliberately not here.
    /// </summary>
    public static readonly HashSet<string> UtilityWords = new(StringComparer.Ordinal)
    {
        "ADD", "ALTER", "DROP", "RENAME", "RESET", "OWNER", "CASCADE", "RESTRICT", "VALIDATE", "ENABLE", "DISABLE", "REPLICA", "ALWAYS",
        "IDENTITY", "LOGGED", "UNLOGGED", "INHERIT", "NO", "OF", "ATTACH", "DETACH", "PARTITION", "WITHOUT", "CLUSTER", "TABLESPACE",
        "STORAGE", "PRIVILEGES", "OPTION", "TABLES", "SEQUENCES", "FUNCTIONS", "PROCEDURES", "ROUTINES", "SCHEMAS", "INSERT", "UPDATE",
        "DELETE", "TRUNCATE", "USAGE", "EXECUTE", "CONNECT", "TEMPORARY", "TEMP", "EXTENSION", "SCHEMA", "SEQUENCE", "FUNCTION",
        "PROCEDURE", "ROUTINE", "DATABASE", "ROLE", "MATERIALIZED", "VIEW", "INDEX", "TRIGGER", "RULE", "POLICY", "DOMAIN", "COLLATION",
        "CONVERSION", "LANGUAGE", "OPERATOR", "ENUM", "MERGE", "WHEN", "MATCHED", "DO", "NOTHING", "BY", "RESTART", "CONTINUE", "RETURNS",
        "IMMUTABLE", "STABLE", "VOLATILE", "STRICT", "SECURITY", "DEFINER", "INVOKER", "PARALLEL", "SAFE", "UNSAFE", "RESTRICTED", "COST",
        "SETOF", "LEAKPROOF", "CALLED", "INPUT", "SUPPORT", "RETURN", "BEGIN", "ATOMIC", "OUT", "INOUT", "OWNED", "ACCESS", "SHARE",
        "EXCLUSIVE", "ROW", "MODE", "NOWAIT", "LOCK", "REINDEX", "REFRESH", "LISTEN", "UNLISTEN", "NOTIFY", "PREPARE", "DEALLOCATE",
        "SAVEPOINT", "RELEASE", "ROLLBACK", "COMMIT", "START", "TRANSACTION", "WORK", "ISOLATION", "LEVEL", "READ", "WRITE", "SERIALIZABLE",
        "REPEATABLE", "COMMITTED", "UNCOMMITTED", "LOCAL", "SESSION", "PASSWORD", "LOGIN", "NOLOGIN", "SUPERUSER", "CREATEDB", "CREATEROLE",
        "VALID", "UNTIL", "SHOW", "SET", "ANALYZE", "ANALYSE", "VACUUM", "FREEZE", "VERBOSE", "CONCURRENTLY", "IF", "EXISTS", "ON",
        "TO", "FROM", "ONLY", "CURRENT", "KEY", "FOREIGN", "REFERENCES", "DEFERRABLE", "INITIALLY", "DEFERRED", "IMMEDIATE", "STATISTICS",
        "COMMENT", "IS", "GRANT", "REVOKE", "ALL", "FORCE", "ROW", "LEVEL", "SECURITY", "BYPASSRLS", "REPLICATION", "CASCADED",
        "CHECK",
    };

    /// <summary>Options of EXPLAIN inside its parentheses.</summary>
    public static readonly HashSet<string> ExplainOptions = new(StringComparer.Ordinal)
    {
        "ANALYZE", "ANALYSE", "VERBOSE", "COSTS", "SETTINGS", "GENERIC_PLAN", "BUFFERS", "SERIALIZE", "WAL", "TIMING", "SUMMARY", "MEMORY",
        "FORMAT", "ON", "OFF", "TRUE", "FALSE", "BINARY", "NONE",
    };

    /// <summary>Operators that compare; spacing around them follows the comparison option.</summary>
    public static readonly HashSet<string> ComparisonOperators = new(StringComparer.Ordinal) { "=", "<>", "!=", "<", ">", "<=", ">=" };

    /// <summary>Operators that calculate; spacing around them follows the arithmetic option.</summary>
    public static readonly HashSet<string> ArithmeticOperators = new(StringComparer.Ordinal) { "+", "-", "*", "/", "%", "^" };

    /// <summary>Data types written with more than one word, longest first.</summary>
    public static readonly string[][] MultiWordTypes =
    {
        new[] { "TIMESTAMP", "WITHOUT", "TIME", "ZONE" },
        new[] { "TIMESTAMP", "WITH", "TIME", "ZONE" },
        new[] { "TIME", "WITHOUT", "TIME", "ZONE" },
        new[] { "TIME", "WITH", "TIME", "ZONE" },
        new[] { "DOUBLE", "PRECISION" },
        new[] { "CHARACTER", "VARYING" },
        new[] { "CHAR", "VARYING" },
        new[] { "BIT", "VARYING" },
        new[] { "NATIONAL", "CHARACTER", "VARYING" },
        new[] { "NATIONAL", "CHARACTER" },
        new[] { "NATIONAL", "CHAR" },
    };

    /// <summary>Single-word built-in data types the casing option applies to when they stand where a type goes.</summary>
    public static readonly HashSet<string> BuiltInTypes = new(StringComparer.Ordinal)
    {
        "SMALLINT", "INTEGER", "INT", "BIGINT", "INT2", "INT4", "INT8", "DECIMAL", "NUMERIC", "DEC", "REAL", "FLOAT", "FLOAT4", "FLOAT8",
        "SERIAL", "SMALLSERIAL", "BIGSERIAL", "SERIAL2", "SERIAL4", "SERIAL8", "MONEY", "CHAR", "CHARACTER", "VARCHAR", "TEXT", "BPCHAR",
        "NAME", "BYTEA", "TIMESTAMP", "TIMESTAMPTZ", "DATE", "TIME", "TIMETZ", "INTERVAL", "BOOLEAN", "BOOL", "ENUM", "POINT", "LINE",
        "LSEG", "BOX", "PATH", "POLYGON", "CIRCLE", "CIDR", "INET", "MACADDR", "MACADDR8", "BIT", "VARBIT", "TSVECTOR", "TSQUERY", "UUID",
        "XML", "JSON", "JSONB", "JSONPATH", "INT4RANGE", "INT8RANGE", "NUMRANGE", "TSRANGE", "TSTZRANGE", "DATERANGE", "OID", "REGCLASS",
        "REGPROC", "REGTYPE", "RECORD", "VOID",
    };

    /// <summary>
    /// Words that begin a statement or a clause and are not reserved, so a name can be spelled the same. The formatter treats them as
    /// keywords only where the grammar puts a keyword (the start of a statement, after <c>ON</c>, and so on).
    /// </summary>
    public static readonly HashSet<string> StatementStarters = new(StringComparer.Ordinal)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "WITH", "VALUES", "TABLE", "MERGE", "CREATE", "ALTER", "DROP", "TRUNCATE", "GRANT",
        "REVOKE", "COMMENT", "EXPLAIN", "ANALYZE", "VACUUM", "COPY", "BEGIN", "COMMIT", "ROLLBACK", "SET", "RESET", "SHOW", "DO",
        "CALL", "PREPARE", "EXECUTE", "DEALLOCATE", "LOCK", "REFRESH", "REINDEX", "CLUSTER", "LISTEN", "NOTIFY", "UNLISTEN", "SAVEPOINT",
        "RELEASE", "START", "END", "ABORT", "DECLARE", "FETCH", "MOVE", "CLOSE", "DISCARD", "IMPORT", "SECURITY", "CHECKPOINT",
    };
}
