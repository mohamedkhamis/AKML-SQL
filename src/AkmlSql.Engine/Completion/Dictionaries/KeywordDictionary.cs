using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Completion.Dictionaries;

/// <summary>
/// Static dictionary of T-SQL keywords grouped by context and SQL Server version.
/// Covers the full T-SQL surface area including DML, DDL, DCL, clauses, functions,
/// data types, control flow, hints, SET options, cursor, fulltext, XML, window frame,
/// and version-specific keywords for SQL Server 2016–2025.
/// </summary>
public static class KeywordDictionary
{
    // SQL Server version thresholds for version-aware keywords
    public const int SqlServer2016 = 2016;
    public const int SqlServer2017 = 2017;
    public const int SqlServer2019 = 2019;
    public const int SqlServer2022 = 2022;
    public const int SqlServer2025 = 2025;

    // ────────────────────────────────────────────────────────────────
    //  DML
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DML keywords — SELECT, INSERT, UPDATE, DELETE, MERGE, etc.
    /// </summary>
    public static readonly string[] Dml =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
        "TRUNCATE", "BULK INSERT", "READTEXT", "WRITETEXT", "UPDATETEXT"
    ];

    // ────────────────────────────────────────────────────────────────
    //  DDL
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DDL keywords — CREATE, ALTER, DROP and object types.
    /// </summary>
    public static readonly string[] Ddl =
    [
        "CREATE", "ALTER", "DROP",
        "TABLE", "VIEW", "INDEX", "PROCEDURE", "FUNCTION", "TRIGGER",
        "SCHEMA", "DATABASE", "SEQUENCE", "TYPE", "SYNONYM",
        "CONSTRAINT", "DEFAULT", "RULE", "STATISTICS",
        // Extended DDL object types
        "PARTITION", "PARTITION FUNCTION", "PARTITION SCHEME",
        "FILEGROUP", "ASSEMBLY", "CERTIFICATE", "CREDENTIAL",
        "LOGIN", "USER", "ROLE", "APPLICATION ROLE",
        "SECURITY POLICY", "COLUMN ENCRYPTION KEY", "COLUMN MASTER KEY",
        "EXTERNAL TABLE", "EXTERNAL DATA SOURCE", "EXTERNAL FILE FORMAT",
        "FULLTEXT INDEX", "FULLTEXT CATALOG",
        "SERVER AUDIT", "DATABASE AUDIT SPECIFICATION",
        // ALTER INDEX sub-keywords
        "REBUILD", "REORGANIZE", "DISABLE", "ENABLE"
    ];

    // ────────────────────────────────────────────────────────────────
    //  DCL / Security
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DCL (Data Control Language) and security-related keywords.
    /// </summary>
    public static readonly string[] Dcl =
    [
        "GRANT", "REVOKE", "DENY",
        "BACKUP", "BACKUP DATABASE", "BACKUP LOG",
        "RESTORE", "RESTORE DATABASE", "RESTORE LOG",
        "OPEN", "CLOSE", "KILL",
        "SHUTDOWN", "RECONFIGURE", "CHECKPOINT",
        "DBCC",
        "USE", "GO"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Clauses
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clause keywords that follow statements.
    /// </summary>
    public static readonly string[] Clauses =
    [
        "FROM", "WHERE", "JOIN", "INNER JOIN", "LEFT JOIN", "RIGHT JOIN",
        "FULL JOIN", "CROSS JOIN", "LEFT OUTER JOIN", "RIGHT OUTER JOIN",
        "FULL OUTER JOIN", "CROSS APPLY", "OUTER APPLY",
        "ON", "AND", "OR", "NOT",
        "GROUP BY", "HAVING", "ORDER BY",
        "UNION", "UNION ALL", "INTERSECT", "EXCEPT",
        "INTO", "VALUES", "SET", "OUTPUT",
        "TOP", "DISTINCT", "AS", "WITH", "OPTION",
        // Previously missing clause keywords
        "PIVOT", "UNPIVOT", "TABLESAMPLE",
        "OVER", "PARTITION BY",
        "WITHIN GROUP",
        "FOR XML", "FOR XML RAW", "FOR XML AUTO", "FOR XML PATH", "FOR XML EXPLICIT",
        "FOR JSON", "FOR JSON PATH", "FOR JSON AUTO",
        "RETURNS", "RETURNS TABLE",
        "WHERE CURRENT OF"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Window frame
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Window frame specification keywords used with OVER().
    /// </summary>
    public static readonly string[] WindowFrame =
    [
        "ROWS", "RANGE", "GROUPS",
        "BETWEEN", "AND",
        "UNBOUNDED PRECEDING", "UNBOUNDED FOLLOWING",
        "CURRENT ROW",
        "PRECEDING", "FOLLOWING"
    ];

    // ────────────────────────────────────────────────────────────────
    //  JOIN types
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// JOIN type keywords.
    /// </summary>
    public static readonly string[] JoinTypes =
    [
        "INNER", "LEFT", "RIGHT", "CROSS", "FULL", "OUTER",
        "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "CROSS JOIN",
        "FULL JOIN", "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "FULL OUTER JOIN",
        "CROSS APPLY", "OUTER APPLY"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Predicates & operators
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Predicate and operator keywords.
    /// </summary>
    public static readonly string[] Predicates =
    [
        "IN", "EXISTS", "BETWEEN", "LIKE", "IS NULL", "IS NOT NULL",
        "ANY", "ALL", "SOME", "NOT IN", "NOT EXISTS", "NOT LIKE",
        "NOT BETWEEN", "ESCAPE"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Scalar / aggregate / window / system functions
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Common built-in scalar functions.
    /// </summary>
    public static readonly string[] ScalarFunctions =
    [
        // ── String ──
        "LEN", "DATALENGTH", "CHARINDEX", "PATINDEX", "REPLACE", "STUFF",
        "SUBSTRING", "LEFT", "RIGHT", "LTRIM", "RTRIM", "TRIM",
        "UPPER", "LOWER", "REVERSE", "REPLICATE", "SPACE",
        "CONCAT", "CONCAT_WS", "STRING_AGG", "FORMAT", "TRANSLATE",
        "CHAR", "ASCII", "UNICODE", "NCHAR", "QUOTENAME",
        "STRING_ESCAPE", "SOUNDEX", "DIFFERENCE",

        // ── Math ──
        "ABS", "CEILING", "FLOOR", "ROUND", "SIGN", "POWER", "SQRT",
        "LOG", "LOG10", "EXP", "RAND", "PI",
        "ACOS", "ASIN", "ATAN", "ATN2", "COS", "COT", "DEGREES",
        "RADIANS", "SIN", "TAN", "SQUARE",

        // ── Date/Time ──
        "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME",
        "SYSDATETIMEOFFSET", "CURRENT_TIMESTAMP",
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATENAME", "DATEPART",
        "DAY", "MONTH", "YEAR", "EOMONTH", "DATEFROMPARTS",
        "DATETIME2FROMPARTS", "DATETIMEFROMPARTS",
        "DATETIMEOFFSETFROMPARTS", "SMALLDATETIMEFROMPARTS", "TIMEFROMPARTS",
        "ISDATE", "SWITCHOFFSET", "TODATETIMEOFFSET",

        // ── Conversion ──
        "CAST", "CONVERT", "TRY_CAST", "TRY_CONVERT", "PARSE", "TRY_PARSE",

        // ── Null handling ──
        "ISNULL", "COALESCE", "NULLIF", "IIF", "CHOOSE",

        // ── Aggregate ──
        "COUNT", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP",
        "COUNT_BIG", "CHECKSUM_AGG", "GROUPING", "GROUPING_ID",
        "APPROX_COUNT_DISTINCT",

        // ── Window / ranking ──
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE",
        "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE",
        "PERCENT_RANK", "CUME_DIST", "PERCENTILE_CONT", "PERCENTILE_DISC",

        // ── System / metadata ──
        "NEWID", "NEWSEQUENTIALID", "SCOPE_IDENTITY", "IDENT_CURRENT",
        "@@IDENTITY", "@@ROWCOUNT", "@@ERROR", "@@TRANCOUNT",
        "@@SPID", "@@SERVERNAME", "@@VERSION", "@@DBTS",
        "@@FETCH_STATUS", "@@CURSOR_ROWS",
        "DB_ID", "DB_NAME", "OBJECT_ID", "OBJECT_NAME",
        "OBJECT_SCHEMA_NAME", "OBJECT_DEFINITION",
        "SCHEMA_ID", "SCHEMA_NAME", "TYPE_ID", "TYPE_NAME",
        "COL_NAME", "COL_LENGTH", "COLUMNPROPERTY", "INDEX_COL",
        "PARSENAME", "SERVERPROPERTY", "DATABASEPROPERTYEX",
        "HOST_NAME", "APP_NAME", "ORIGINAL_LOGIN",
        "SUSER_SNAME", "SUSER_SID", "SUSER_ID",
        "USER_NAME", "USER_ID", "SYSTEM_USER", "SESSION_USER", "CURRENT_USER",
        "HAS_PERMS_BY_NAME", "IS_MEMBER", "IS_SRVROLEMEMBER", "IS_ROLEMEMBER",
        "HAS_DBACCESS",

        // ── Security / crypto ──
        "HASHBYTES", "COMPRESS", "DECOMPRESS",
        "ENCRYPTBYKEY", "DECRYPTBYKEY", "ENCRYPTBYPASSPHRASE", "DECRYPTBYPASSPHRASE",
        "CERTENCODED", "CERTPRIVATEKEY",

        // ── JSON (2016+) ──
        "JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "ISJSON", "OPENJSON",

        // ── CASE expression ──
        "CASE", "WHEN", "THEN", "ELSE", "END"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Data types
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Common T-SQL data types.
    /// </summary>
    public static readonly string[] DataTypes =
    [
        "INT", "BIGINT", "SMALLINT", "TINYINT", "BIT",
        "DECIMAL", "NUMERIC", "FLOAT", "REAL", "MONEY", "SMALLMONEY",
        "CHAR", "VARCHAR", "NCHAR", "NVARCHAR", "TEXT", "NTEXT",
        "DATE", "TIME", "DATETIME", "DATETIME2", "SMALLDATETIME", "DATETIMEOFFSET",
        "BINARY", "VARBINARY", "IMAGE",
        "UNIQUEIDENTIFIER", "XML", "SQL_VARIANT",
        "GEOGRAPHY", "GEOMETRY", "HIERARCHYID",
        "ROWVERSION", "TIMESTAMP", "CURSOR", "TABLE",
        // Extended / less common types
        "SYSNAME", "MAX"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Control flow
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Transaction/control flow keywords.
    /// </summary>
    public static readonly string[] ControlFlow =
    [
        "BEGIN", "END", "BEGIN TRANSACTION", "COMMIT", "ROLLBACK", "SAVE TRANSACTION",
        "IF", "ELSE", "WHILE", "BREAK", "CONTINUE", "RETURN",
        "TRY", "CATCH", "THROW", "RAISERROR",
        "BEGIN TRY", "END TRY", "BEGIN CATCH", "END CATCH",
        "GOTO", "WAITFOR", "WAITFOR DELAY", "WAITFOR TIME",
        "PRINT", "EXEC", "EXECUTE",
        "DECLARE", "SET",
        // Transaction isolation
        "BEGIN DISTRIBUTED TRANSACTION",
        "SAVE"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Cursor
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cursor declaration, manipulation, and attribute keywords.
    /// </summary>
    public static readonly string[] CursorKeywords =
    [
        // Lifecycle
        "DECLARE CURSOR", "OPEN", "CLOSE", "DEALLOCATE",
        // Fetch directions
        "FETCH", "FETCH NEXT", "FETCH PRIOR", "FETCH FIRST", "FETCH LAST",
        "FETCH ABSOLUTE", "FETCH RELATIVE",
        // Cursor types
        "SCROLL", "NO_SCROLL",
        "STATIC", "DYNAMIC", "FAST_FORWARD", "KEYSET",
        "FORWARD_ONLY",
        // Concurrency
        "READ_ONLY", "SCROLL_LOCKS", "OPTIMISTIC",
        // Scope
        "LOCAL", "GLOBAL",
        "INSENSITIVE",
        // Status variables
        "@@FETCH_STATUS", "@@CURSOR_ROWS", "CURSOR_STATUS"
    ];

    // ────────────────────────────────────────────────────────────────
    //  SET options
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// SET option keywords commonly used in SSMS sessions.
    /// </summary>
    public static readonly string[] SetOptions =
    [
        // ANSI settings
        "SET ANSI_NULLS", "SET ANSI_PADDING", "SET ANSI_WARNINGS",
        "SET ANSI_DEFAULTS", "SET ARITHABORT", "SET ARITHIGNORE",
        "SET QUOTED_IDENTIFIER", "SET CONCAT_NULL_YIELDS_NULL",
        "SET NUMERIC_ROUNDABORT",
        // Execution control
        "SET NOCOUNT", "SET NOEXEC", "SET PARSEONLY", "SET FMTONLY",
        "SET ROWCOUNT", "SET TEXTSIZE",
        "SET XACT_ABORT",
        "SET IDENTITY_INSERT",
        // Transaction isolation
        "SET TRANSACTION ISOLATION LEVEL",
        "READ UNCOMMITTED", "READ COMMITTED", "REPEATABLE READ",
        "SERIALIZABLE", "SNAPSHOT",
        // Date/language
        "SET DATEFORMAT", "SET DATEFIRST", "SET LANGUAGE",
        // Deadlock / lock
        "SET DEADLOCK_PRIORITY", "SET LOCK_TIMEOUT",
        // Statistics / plan display
        "SET STATISTICS IO", "SET STATISTICS TIME", "SET STATISTICS PROFILE",
        "SET STATISTICS XML",
        "SET SHOWPLAN_ALL", "SET SHOWPLAN_TEXT", "SET SHOWPLAN_XML",
        // Other
        "SET CONTEXT_INFO",
        "SET IMPLICIT_TRANSACTIONS",
        "SET CURSOR_CLOSE_ON_COMMIT",
        "ON", "OFF"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Table hints
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Table hint keywords used with WITH (...) on table references.
    /// </summary>
    public static readonly string[] TableHints =
    [
        // Locking hints
        "NOLOCK", "READUNCOMMITTED", "READCOMMITTED", "READCOMMITTEDLOCK",
        "REPEATABLEREAD", "SERIALIZABLE",
        "TABLOCK", "TABLOCKX", "UPDLOCK", "HOLDLOCK",
        "ROWLOCK", "PAGLOCK", "XLOCK", "NOWAIT", "READPAST",
        // Scan hints
        "FORCESEEK", "FORCESCAN", "NOEXPAND",
        // Index hint
        "INDEX",
        // Insert hints
        "KEEPIDENTITY", "KEEPDEFAULTS", "IGNORE_DUP_KEY", "IGNORE_CONSTRAINTS",
        "IGNORE_TRIGGERS",
        // Spatial
        "SPATIAL_WINDOW_MAX_CELLS"
    ];

    /// <summary>
    /// Query-level hint keywords used with OPTION (...).
    /// </summary>
    public static readonly string[] QueryHints =
    [
        "RECOMPILE", "MAXDOP", "MAXRECURSION",
        "OPTIMIZE FOR", "OPTIMIZE FOR UNKNOWN",
        "FAST", "FORCE ORDER", "KEEP PLAN", "KEEPFIXED PLAN",
        "EXPAND VIEWS", "NO_PERFORMANCE_SPOOL",
        "HASH JOIN", "LOOP JOIN", "MERGE JOIN",
        "HASH GROUP", "ORDER GROUP",
        "HASH UNION", "CONCAT UNION", "MERGE UNION",
        "PARAMETERIZATION SIMPLE", "PARAMETERIZATION FORCED",
        "USE PLAN", "USE HINT",
        "MAX_GRANT_PERCENT", "MIN_GRANT_PERCENT",
        "QUERYTRACEON", "DISABLE_OPTIMIZED_PLAN_FORCING",
        "IGNORE_NONCLUSTERED_COLUMNSTORE_INDEX"
    ];

    /// <summary>
    /// Join hint keywords.
    /// </summary>
    public static readonly string[] JoinHints =
    [
        "HASH", "LOOP", "MERGE", "REMOTE"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Fulltext & semantic search
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full-text search and semantic search keywords (reserved in SQL Server).
    /// </summary>
    public static readonly string[] FullTextKeywords =
    [
        "CONTAINS", "CONTAINSTABLE",
        "FREETEXT", "FREETEXTTABLE",
        "SEMANTICKEYPHRASETABLE",
        "SEMANTICSIMILARITYTABLE",
        "SEMANTICSIMILARITYDETAILSTABLE",
        // Full-text predicates
        "FORMSOF", "INFLECTIONAL", "THESAURUS",
        "ISABOUT", "WEIGHT",
        "NEAR"
    ];

    // ────────────────────────────────────────────────────────────────
    //  XML
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// XML-related keywords and XQuery methods.
    /// </summary>
    public static readonly string[] XmlKeywords =
    [
        // FOR XML modes
        "FOR XML", "FOR XML RAW", "FOR XML AUTO", "FOR XML PATH", "FOR XML EXPLICIT",
        "ELEMENTS", "XSINIL", "ROOT", "TYPE",
        // XML schema
        "WITH XMLNAMESPACES", "XMLNAMESPACES",
        // OPENXML
        "OPENXML",
        // XQuery methods (used on XML-typed columns)
        ".value()", ".query()", ".nodes()", ".exist()", ".modify()",
        // XML DML
        "XMLPARSE", "XMLSERIALIZE"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Constraint / DDL modifier keywords
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constraint and DDL modifier keywords used in CREATE/ALTER TABLE.
    /// </summary>
    public static readonly string[] ConstraintKeywords =
    [
        "PRIMARY KEY", "FOREIGN KEY", "REFERENCES", "UNIQUE", "CHECK",
        "CLUSTERED", "NONCLUSTERED", "COLUMNSTORE",
        "CASCADE", "SET NULL", "SET DEFAULT", "NO ACTION", "RESTRICT",
        "NOT NULL", "NULL", "DEFAULT",
        "IDENTITY", "IDENTITY_INSERT",
        "COLLATE", "AUTHORIZATION",
        "FILLFACTOR", "PAD_INDEX",
        "ROWGUIDCOL", "IDENTITYCOL",
        "COMPUTED", "PERSISTED", "SPARSE",
        "NOT FOR REPLICATION",
        "WITH CHECK", "WITH NOCHECK",
        "ADD", "DROP COLUMN",
        "INCLUDE", "WHERE",
        "ON DELETE", "ON UPDATE",
        "WITH", "WITHOUT"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Open* / remote data keywords
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Remote / linked server / rowset function keywords.
    /// </summary>
    public static readonly string[] RowsetFunctions =
    [
        "OPENQUERY", "OPENROWSET", "OPENDATASOURCE", "OPENXML", "OPENJSON",
        "LINKED SERVER"
    ];

    // ────────────────────────────────────────────────────────────────
    //  Version-specific keywords
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Keywords added in SQL Server 2019.
    /// </summary>
    public static readonly string[] SqlServer2019Keywords =
    [
        "APPROX_COUNT_DISTINCT",
        "ACCELERATED_DATABASE_RECOVERY"
    ];

    /// <summary>
    /// Keywords added in SQL Server 2022+.
    /// </summary>
    public static readonly string[] SqlServer2022Keywords =
    [
        // Scalar functions
        "GREATEST", "LEAST", "GENERATE_SERIES",
        "DATE_BUCKET", "DATETRUNC",
        // STRING_SPLIT with ordinal is an enhancement, not a new keyword
        "STRING_SPLIT",
        // JSON constructors
        "JSON_OBJECT", "JSON_ARRAY",
        // Null-safe comparison
        "IS DISTINCT FROM", "IS NOT DISTINCT FROM",
        // WINDOW clause (named window definitions)
        "WINDOW",
        // TRIM enhancements
        "LEADING", "TRAILING", "BOTH",
        // Window function null handling
        "IGNORE NULLS", "RESPECT NULLS",
        // Bit manipulation functions
        "BIT_COUNT", "GET_BIT", "SET_BIT",
        "LEFT_SHIFT", "RIGHT_SHIFT",
        "BIT_AND", "BIT_OR", "BIT_XOR"
    ];

    /// <summary>
    /// Keywords added in SQL Server 2025+.
    /// </summary>
    public static readonly string[] SqlServer2025Keywords =
    [
        // JSON aggregates
        "JSON_OBJECTAGG", "JSON_ARRAYAGG",
        // Vector/AI
        "VECTOR", "VECTOR_DISTANCE",
        // Native JSON data type
        "JSON",
        // New regex support
        "REGEX", "REGEXP_LIKE", "REGEXP_COUNT", "REGEXP_INSTR",
        "REGEXP_REPLACE", "REGEXP_SUBSTR"
    ];

    // ────────────────────────────────────────────────────────────────
    //  GetKeywordsForClause / GetAllKeywords
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// T053: Get keywords appropriate for the given clause context.
    /// </summary>
    public static IReadOnlyList<string> GetKeywordsForClause(ClauseType clauseType)
    {
        return clauseType switch
        {
            ClauseType.Select      => AfterSelect,
            ClauseType.From        => AfterFrom,
            ClauseType.JoinTable   => AfterJoinTable,
            ClauseType.UpdateTable => AfterUpdateTable,
            ClauseType.Where       => AfterWhere,
            ClauseType.JoinOn      => AfterJoinOn,
            ClauseType.GroupBy     => AfterGroupBy,
            ClauseType.Having      => AfterHaving,
            ClauseType.OrderBy     => AfterOrderBy,
            ClauseType.InsertColumns => AfterInsert,
            ClauseType.UpdateSet   => AfterUpdateSet,
            ClauseType.With        => AfterWith,
            ClauseType.Over        => AfterOver,
            ClauseType.Option      => AfterOption,
            ClauseType.Set         => AfterSet,
            ClauseType.Declare     => AfterDeclare,
            ClauseType.Create      => AfterCreate,
            ClauseType.Alter       => AfterAlter,
            ClauseType.Drop        => AfterDrop,
            ClauseType.Grant       => AfterGrant,
            ClauseType.ForXml      => AfterForXml,
            ClauseType.ForJson     => AfterForJson,
            ClauseType.Exec        => [],
            _                      => GeneralKeywords
        };
    }

    /// <summary>
    /// Get all keywords for a given SQL Server version.
    /// </summary>
    public static IReadOnlyList<string> GetAllKeywords(int sqlServerVersion = SqlServer2022)
    {
        var result = new List<string>(512);
        result.AddRange(Dml);
        result.AddRange(Ddl);
        result.AddRange(Dcl);
        result.AddRange(Clauses);
        result.AddRange(WindowFrame);
        result.AddRange(Predicates);
        result.AddRange(ScalarFunctions);
        result.AddRange(DataTypes);
        result.AddRange(ControlFlow);
        result.AddRange(CursorKeywords);
        result.AddRange(SetOptions);
        result.AddRange(TableHints);
        result.AddRange(QueryHints);
        result.AddRange(JoinHints);
        result.AddRange(FullTextKeywords);
        result.AddRange(XmlKeywords);
        result.AddRange(ConstraintKeywords);
        result.AddRange(RowsetFunctions);

        if (sqlServerVersion >= SqlServer2019)
        {
            result.AddRange(SqlServer2019Keywords);
        }

        if (sqlServerVersion >= SqlServer2022)
        {
            result.AddRange(SqlServer2022Keywords);
        }

        if (sqlServerVersion >= SqlServer2025)
        {
            result.AddRange(SqlServer2025Keywords);
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────────
    //  Clause-to-keyword mappings
    // ────────────────────────────────────────────────────────────────

    private static readonly string[] AfterSelect =
    [
        "TOP", "DISTINCT", "INTO", "AS",
        "CASE", "WHEN", "CAST", "CONVERT", "COALESCE", "ISNULL", "NULLIF", "IIF",
        "COUNT", "SUM", "AVG", "MIN", "MAX",
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE",
        "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE",
        "STRING_AGG", "CONCAT", "CONCAT_WS",
        "EXISTS",
        "FROM"
    ];

    private static readonly string[] AfterUpdateTable =
    [
        "SET", "AS"
    ];

    private static readonly string[] AfterJoinTable =
    [
        "AS",
        "WITH", "NOLOCK", "READUNCOMMITTED", "READCOMMITTED",
        "REPEATABLEREAD", "SERIALIZABLE", "TABLOCK", "TABLOCKX",
        "UPDLOCK", "HOLDLOCK", "ROWLOCK", "PAGLOCK",
        "XLOCK", "NOWAIT", "READPAST",
        "FORCESEEK", "FORCESCAN", "NOEXPAND", "INDEX"
    ];

    private static readonly string[] AfterFrom =
    [
        "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "CROSS JOIN",
        "FULL JOIN", "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "FULL OUTER JOIN",
        "CROSS APPLY", "OUTER APPLY",
        "JOIN", "WHERE", "GROUP BY", "HAVING", "ORDER BY",
        "UNION", "UNION ALL", "INTERSECT", "EXCEPT",
        "ON", "AS",
        "WITH", "NOLOCK", "READUNCOMMITTED", "READCOMMITTED",
        "REPEATABLEREAD", "SERIALIZABLE", "TABLOCK", "TABLOCKX",
        "UPDLOCK", "HOLDLOCK", "ROWLOCK", "PAGLOCK",
        "XLOCK", "NOWAIT", "READPAST",
        "FORCESEEK", "FORCESCAN", "NOEXPAND", "INDEX",
        "PIVOT", "UNPIVOT", "TABLESAMPLE",
        "FOR XML", "FOR JSON",
        "OPTION"
    ];

    private static readonly string[] AfterWhere =
    [
        "AND", "OR", "NOT",
        "IN", "EXISTS", "BETWEEN", "LIKE",
        "IS NULL", "IS NOT NULL",
        "ANY", "ALL", "SOME",
        "CASE", "CAST", "CONVERT",
        "GROUP BY", "HAVING", "ORDER BY",
        "UNION", "UNION ALL", "INTERSECT", "EXCEPT",
        // Fulltext predicates
        "CONTAINS", "FREETEXT"
    ];

    private static readonly string[] AfterJoinOn =
    [
        "AND", "OR",
        "WHERE", "GROUP BY", "HAVING", "ORDER BY",
        "UNION", "UNION ALL", "INTERSECT", "EXCEPT",
        "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "CROSS JOIN", "FULL JOIN",
        "CROSS APPLY", "OUTER APPLY"
    ];

    private static readonly string[] AfterGroupBy =
    [
        "HAVING", "ORDER BY",
        "WITH ROLLUP", "WITH CUBE",
        "GROUPING SETS",
        "FOR XML", "FOR JSON",
        "OPTION"
    ];

    private static readonly string[] AfterHaving =
    [
        "AND", "OR", "NOT",
        "ORDER BY",
        "COUNT", "SUM", "AVG", "MIN", "MAX",
        "FOR XML", "FOR JSON",
        "OPTION"
    ];

    private static readonly string[] AfterOrderBy =
    [
        "ASC", "DESC", "OFFSET", "FETCH NEXT", "ROWS ONLY",
        "NULLS FIRST", "NULLS LAST",
        "FOR XML", "FOR JSON",
        "OPTION"
    ];

    private static readonly string[] AfterInsert =
    [
        "VALUES", "SELECT", "DEFAULT VALUES", "OUTPUT", "EXEC", "EXECUTE",
        "TOP"
    ];

    private static readonly string[] AfterUpdateSet =
    [
        "WHERE", "FROM", "OUTPUT",
        "OPTION"
    ];

    private static readonly string[] AfterWith =
    [
        "AS", "NOLOCK", "READUNCOMMITTED",
        "READCOMMITTED", "REPEATABLEREAD", "SERIALIZABLE",
        "TABLOCK", "TABLOCKX", "UPDLOCK", "HOLDLOCK",
        "ROWLOCK", "PAGLOCK", "XLOCK", "NOWAIT", "READPAST",
        "FORCESEEK", "FORCESCAN", "NOEXPAND", "INDEX",
        "KEEPIDENTITY", "KEEPDEFAULTS",
        "IGNORE_DUP_KEY", "IGNORE_CONSTRAINTS", "IGNORE_TRIGGERS",
        // CTE
        "RECURSIVE"
    ];

    /// <summary>
    /// Keywords after OVER( — window specification context.
    /// </summary>
    private static readonly string[] AfterOver =
    [
        "PARTITION BY", "ORDER BY",
        "ROWS", "RANGE", "GROUPS",
        "BETWEEN",
        "UNBOUNDED PRECEDING", "UNBOUNDED FOLLOWING",
        "CURRENT ROW", "PRECEDING", "FOLLOWING"
    ];

    /// <summary>
    /// Keywords after OPTION( — query hint context.
    /// </summary>
    private static readonly string[] AfterOption =
    [
        "RECOMPILE", "MAXDOP", "MAXRECURSION",
        "OPTIMIZE FOR", "OPTIMIZE FOR UNKNOWN",
        "FAST", "FORCE ORDER", "KEEP PLAN", "KEEPFIXED PLAN",
        "EXPAND VIEWS", "NO_PERFORMANCE_SPOOL",
        "HASH JOIN", "LOOP JOIN", "MERGE JOIN",
        "HASH GROUP", "ORDER GROUP",
        "HASH UNION", "CONCAT UNION", "MERGE UNION",
        "PARAMETERIZATION SIMPLE", "PARAMETERIZATION FORCED",
        "USE PLAN", "USE HINT",
        "MAX_GRANT_PERCENT", "MIN_GRANT_PERCENT",
        "QUERYTRACEON",
        "DISABLE_OPTIMIZED_PLAN_FORCING",
        "IGNORE_NONCLUSTERED_COLUMNSTORE_INDEX"
    ];

    /// <summary>
    /// Keywords after SET — SET option context.
    /// </summary>
    private static readonly string[] AfterSet =
    [
        "NOCOUNT", "ANSI_NULLS", "ANSI_PADDING", "ANSI_WARNINGS",
        "ARITHABORT", "ARITHIGNORE",
        "QUOTED_IDENTIFIER", "CONCAT_NULL_YIELDS_NULL",
        "NUMERIC_ROUNDABORT",
        "NOEXEC", "PARSEONLY", "FMTONLY",
        "ROWCOUNT", "TEXTSIZE",
        "XACT_ABORT",
        "IDENTITY_INSERT",
        "TRANSACTION ISOLATION LEVEL",
        "DATEFORMAT", "DATEFIRST", "LANGUAGE",
        "DEADLOCK_PRIORITY", "LOCK_TIMEOUT",
        "STATISTICS IO", "STATISTICS TIME", "STATISTICS PROFILE", "STATISTICS XML",
        "SHOWPLAN_ALL", "SHOWPLAN_TEXT", "SHOWPLAN_XML",
        "CONTEXT_INFO",
        "IMPLICIT_TRANSACTIONS",
        "CURSOR_CLOSE_ON_COMMIT",
        "ON", "OFF"
    ];

    /// <summary>
    /// Keywords after DECLARE — variable/cursor/table declaration context.
    /// </summary>
    private static readonly string[] AfterDeclare =
    [
        // Data types for variables
        "INT", "BIGINT", "SMALLINT", "TINYINT", "BIT",
        "DECIMAL", "NUMERIC", "FLOAT", "REAL", "MONEY", "SMALLMONEY",
        "CHAR", "VARCHAR", "NCHAR", "NVARCHAR",
        "DATE", "TIME", "DATETIME", "DATETIME2", "DATETIMEOFFSET",
        "BINARY", "VARBINARY",
        "UNIQUEIDENTIFIER", "XML", "SQL_VARIANT", "TABLE",
        "CURSOR",
        "AS"
    ];

    /// <summary>
    /// Keywords after CREATE — object type context.
    /// </summary>
    private static readonly string[] AfterCreate =
    [
        "TABLE", "VIEW", "INDEX", "UNIQUE INDEX",
        "CLUSTERED INDEX", "NONCLUSTERED INDEX", "COLUMNSTORE INDEX",
        "PROCEDURE", "PROC", "FUNCTION", "TRIGGER",
        "SCHEMA", "DATABASE", "SEQUENCE", "TYPE", "SYNONYM",
        "LOGIN", "USER", "ROLE", "APPLICATION ROLE",
        "CERTIFICATE", "CREDENTIAL", "ASSEMBLY",
        "PARTITION FUNCTION", "PARTITION SCHEME",
        "FULLTEXT INDEX", "FULLTEXT CATALOG",
        "SECURITY POLICY",
        "EXTERNAL TABLE", "EXTERNAL DATA SOURCE", "EXTERNAL FILE FORMAT",
        "DEFAULT", "RULE", "STATISTICS",
        "SERVER AUDIT", "DATABASE AUDIT SPECIFICATION",
        "OR ALTER"
    ];

    /// <summary>
    /// Keywords after ALTER — object type context.
    /// </summary>
    private static readonly string[] AfterAlter =
    [
        "TABLE", "VIEW", "INDEX",
        "PROCEDURE", "PROC", "FUNCTION", "TRIGGER",
        "SCHEMA", "DATABASE", "SEQUENCE", "TYPE",
        "LOGIN", "USER", "ROLE", "APPLICATION ROLE",
        "ASSEMBLY", "CERTIFICATE",
        "PARTITION FUNCTION", "PARTITION SCHEME",
        "FULLTEXT INDEX", "FULLTEXT CATALOG",
        "SECURITY POLICY",
        "SERVER AUDIT", "DATABASE AUDIT SPECIFICATION",
        "AUTHORIZATION"
    ];

    /// <summary>
    /// Keywords after DROP — object type context.
    /// </summary>
    private static readonly string[] AfterDrop =
    [
        "TABLE", "VIEW", "INDEX",
        "PROCEDURE", "PROC", "FUNCTION", "TRIGGER",
        "SCHEMA", "DATABASE", "SEQUENCE", "TYPE", "SYNONYM",
        "LOGIN", "USER", "ROLE", "APPLICATION ROLE",
        "ASSEMBLY", "CERTIFICATE", "CREDENTIAL",
        "FULLTEXT INDEX", "FULLTEXT CATALOG",
        "SECURITY POLICY",
        "EXTERNAL TABLE", "EXTERNAL DATA SOURCE", "EXTERNAL FILE FORMAT",
        "DEFAULT", "RULE", "STATISTICS",
        "IF EXISTS"
    ];

    /// <summary>
    /// Keywords after GRANT/REVOKE/DENY — permission context.
    /// </summary>
    private static readonly string[] AfterGrant =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "EXECUTE", "REFERENCES",
        "ALTER", "VIEW DEFINITION", "CONTROL",
        "CREATE TABLE", "CREATE VIEW", "CREATE PROCEDURE", "CREATE FUNCTION",
        "CREATE DATABASE", "CREATE SCHEMA",
        "CONNECT", "TAKE OWNERSHIP",
        "ALL", "ALL PRIVILEGES",
        "ON", "TO", "FROM", "WITH GRANT OPTION",
        "AS"
    ];

    /// <summary>
    /// Keywords after FOR XML — XML output context.
    /// </summary>
    private static readonly string[] AfterForXml =
    [
        "RAW", "AUTO", "PATH", "EXPLICIT",
        "ELEMENTS", "XSINIL", "ROOT", "TYPE",
        "BINARY BASE64"
    ];

    /// <summary>
    /// Keywords after FOR JSON — JSON output context.
    /// </summary>
    private static readonly string[] AfterForJson =
    [
        "PATH", "AUTO",
        "ROOT", "INCLUDE_NULL_VALUES", "WITHOUT_ARRAY_WRAPPER"
    ];

    private static readonly string[] GeneralKeywords =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
        "CREATE", "ALTER", "DROP",
        "GRANT", "REVOKE", "DENY",
        "EXEC", "EXECUTE",
        "BEGIN", "END", "IF", "ELSE", "WHILE",
        "DECLARE", "SET", "PRINT",
        "BEGIN TRANSACTION", "COMMIT", "ROLLBACK",
        "BEGIN TRY", "END TRY", "BEGIN CATCH", "END CATCH",
        "WITH", "GO", "USE",
        "BACKUP", "RESTORE",
        "OPEN", "CLOSE", "FETCH", "DEALLOCATE",
        "RETURN", "THROW", "RAISERROR",
        "WAITFOR"
    ];
}
