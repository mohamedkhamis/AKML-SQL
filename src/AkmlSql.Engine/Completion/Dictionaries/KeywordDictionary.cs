using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Completion.Dictionaries;

/// <summary>
/// Static dictionary of T-SQL keywords grouped by context and SQL Server version.
/// </summary>
public static class KeywordDictionary
{
    // SQL Server version thresholds for version-aware keywords
    public const int SqlServer2016 = 2016;
    public const int SqlServer2017 = 2017;
    public const int SqlServer2019 = 2019;
    public const int SqlServer2022 = 2022;
    public const int SqlServer2025 = 2025;

    /// <summary>
    /// DML keywords — SELECT, INSERT, UPDATE, DELETE, MERGE, etc.
    /// </summary>
    public static readonly string[] Dml =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
        "TRUNCATE", "BULK INSERT", "READTEXT", "WRITETEXT", "UPDATETEXT"
    ];

    /// <summary>
    /// DDL keywords — CREATE, ALTER, DROP, etc.
    /// </summary>
    public static readonly string[] Ddl =
    [
        "CREATE", "ALTER", "DROP",
        "TABLE", "VIEW", "INDEX", "PROCEDURE", "FUNCTION", "TRIGGER",
        "SCHEMA", "DATABASE", "SEQUENCE", "TYPE", "SYNONYM",
        "CONSTRAINT", "DEFAULT", "RULE", "STATISTICS"
    ];

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
        "TOP", "DISTINCT", "AS", "WITH", "OPTION"
    ];

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

    /// <summary>
    /// Predicate and operator keywords.
    /// </summary>
    public static readonly string[] Predicates =
    [
        "IN", "EXISTS", "BETWEEN", "LIKE", "IS NULL", "IS NOT NULL",
        "ANY", "ALL", "SOME", "NOT IN", "NOT EXISTS", "NOT LIKE",
        "NOT BETWEEN", "ESCAPE"
    ];

    /// <summary>
    /// Common built-in scalar functions.
    /// </summary>
    public static readonly string[] ScalarFunctions =
    [
        // String
        "LEN", "DATALENGTH", "CHARINDEX", "PATINDEX", "REPLACE", "STUFF",
        "SUBSTRING", "LEFT", "RIGHT", "LTRIM", "RTRIM", "TRIM",
        "UPPER", "LOWER", "REVERSE", "REPLICATE", "SPACE",
        "CONCAT", "CONCAT_WS", "STRING_AGG", "FORMAT", "TRANSLATE",
        "CHAR", "ASCII", "UNICODE", "NCHAR", "QUOTENAME",
        // Math
        "ABS", "CEILING", "FLOOR", "ROUND", "SIGN", "POWER", "SQRT",
        "LOG", "LOG10", "EXP", "RAND", "PI",
        // Date/Time
        "GETDATE", "GETUTCDATE", "SYSDATETIME", "SYSUTCDATETIME",
        "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATENAME", "DATEPART",
        "DAY", "MONTH", "YEAR", "EOMONTH", "DATEFROMPARTS",
        "DATETIME2FROMPARTS", "DATETIMEFROMPARTS", "ISDATE",
        // Conversion
        "CAST", "CONVERT", "TRY_CAST", "TRY_CONVERT", "PARSE", "TRY_PARSE",
        // Null handling
        "ISNULL", "COALESCE", "NULLIF", "IIF", "CHOOSE",
        // Aggregate
        "COUNT", "SUM", "AVG", "MIN", "MAX", "STDEV", "STDEVP", "VAR", "VARP",
        "COUNT_BIG", "CHECKSUM_AGG", "GROUPING", "GROUPING_ID",
        // Window
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE",
        "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE",
        "PERCENT_RANK", "CUME_DIST", "PERCENTILE_CONT", "PERCENTILE_DISC",
        // System
        "NEWID", "NEWSEQUENTIALID", "SCOPE_IDENTITY", "IDENT_CURRENT",
        "@@IDENTITY", "@@ROWCOUNT", "@@ERROR", "@@TRANCOUNT",
        "DB_ID", "DB_NAME", "OBJECT_ID", "OBJECT_NAME",
        "SCHEMA_ID", "SCHEMA_NAME", "TYPE_ID", "TYPE_NAME",
        // JSON (2016+)
        "JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "ISJSON", "OPENJSON",
        // Other
        "CASE", "WHEN", "THEN", "ELSE", "END"
    ];

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
        "ROWVERSION", "TIMESTAMP", "CURSOR", "TABLE"
    ];

    /// <summary>
    /// Transaction/control flow keywords.
    /// </summary>
    public static readonly string[] ControlFlow =
    [
        "BEGIN", "END", "BEGIN TRANSACTION", "COMMIT", "ROLLBACK", "SAVE TRANSACTION",
        "IF", "ELSE", "WHILE", "BREAK", "CONTINUE", "RETURN",
        "TRY", "CATCH", "THROW", "RAISERROR",
        "BEGIN TRY", "END TRY", "BEGIN CATCH", "END CATCH",
        "GOTO", "WAITFOR", "PRINT", "EXEC", "EXECUTE",
        "DECLARE", "SET"
    ];

    /// <summary>
    /// Keywords added in SQL Server 2022+.
    /// </summary>
    public static readonly string[] SqlServer2022Keywords =
    [
        "GREATEST", "LEAST", "STRING_SPLIT", "GENERATE_SERIES",
        "DATE_BUCKET", "DATETRUNC", "WINDOW", "JSON_OBJECT", "JSON_ARRAY",
        "IS DISTINCT FROM", "IS NOT DISTINCT FROM"
    ];

    /// <summary>
    /// Keywords added in SQL Server 2025+.
    /// </summary>
    public static readonly string[] SqlServer2025Keywords =
    [
        "JSON_OBJECTAGG", "JSON_ARRAYAGG", "VECTOR", "VECTOR_DISTANCE"
    ];

    /// <summary>
    /// T053: Get keywords appropriate for the given clause context.
    /// </summary>
    public static IReadOnlyList<string> GetKeywordsForClause(ClauseType clauseType)
    {
        return clauseType switch
        {
            ClauseType.Select => AfterSelect,
            ClauseType.From => AfterFrom,
            ClauseType.Where => AfterWhere,
            ClauseType.JoinOn => AfterJoinOn,
            ClauseType.GroupBy => AfterGroupBy,
            ClauseType.Having => AfterHaving,
            ClauseType.OrderBy => AfterOrderBy,
            ClauseType.InsertColumns => AfterInsert,
            ClauseType.UpdateSet => AfterUpdateSet,
            ClauseType.With => AfterWith,
            ClauseType.Exec => [],
            _ => GeneralKeywords
        };
    }

    /// <summary>
    /// Get all keywords for a given SQL Server version.
    /// </summary>
    public static IReadOnlyList<string> GetAllKeywords(int sqlServerVersion = SqlServer2022)
    {
        var result = new List<string>(256);
        result.AddRange(Dml);
        result.AddRange(Ddl);
        result.AddRange(Clauses);
        result.AddRange(Predicates);
        result.AddRange(ScalarFunctions);
        result.AddRange(DataTypes);
        result.AddRange(ControlFlow);

        if (sqlServerVersion >= SqlServer2022)
            result.AddRange(SqlServer2022Keywords);

        if (sqlServerVersion >= SqlServer2025)
            result.AddRange(SqlServer2025Keywords);

        return result;
    }

    // T053: Clause-to-keyword mappings

    private static readonly string[] AfterSelect =
    [
        "TOP", "DISTINCT", "INTO", "AS",
        "CASE", "WHEN", "CAST", "CONVERT", "COALESCE", "ISNULL", "NULLIF", "IIF",
        "COUNT", "SUM", "AVG", "MIN", "MAX",
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE",
        "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE",
        "FROM"
    ];

    private static readonly string[] AfterFrom =
    [
        "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "CROSS JOIN",
        "FULL JOIN", "LEFT OUTER JOIN", "RIGHT OUTER JOIN", "FULL OUTER JOIN",
        "CROSS APPLY", "OUTER APPLY",
        "JOIN", "WHERE", "GROUP BY", "HAVING", "ORDER BY",
        "ON", "AS",
        "WITH", "NOLOCK", "READUNCOMMITTED", "READCOMMITTED",
        "REPEATABLEREAD", "SERIALIZABLE", "TABLOCK", "TABLOCKX",
        "UPDLOCK", "HOLDLOCK", "ROWLOCK", "PAGLOCK",
        "PIVOT", "UNPIVOT"
    ];

    private static readonly string[] AfterWhere =
    [
        "AND", "OR", "NOT",
        "IN", "EXISTS", "BETWEEN", "LIKE",
        "IS NULL", "IS NOT NULL",
        "ANY", "ALL", "SOME",
        "CASE", "CAST", "CONVERT",
        "GROUP BY", "HAVING", "ORDER BY",
        "UNION", "UNION ALL", "INTERSECT", "EXCEPT"
    ];

    private static readonly string[] AfterJoinOn =
    [
        "AND", "OR",
        "WHERE", "GROUP BY", "HAVING", "ORDER BY",
        "INNER JOIN", "LEFT JOIN", "RIGHT JOIN", "CROSS JOIN", "FULL JOIN",
        "CROSS APPLY", "OUTER APPLY"
    ];

    private static readonly string[] AfterGroupBy =
    [
        "HAVING", "ORDER BY",
        "WITH ROLLUP", "WITH CUBE",
        "GROUPING SETS"
    ];

    private static readonly string[] AfterHaving =
    [
        "AND", "OR", "NOT",
        "ORDER BY",
        "COUNT", "SUM", "AVG", "MIN", "MAX"
    ];

    private static readonly string[] AfterOrderBy =
    [
        "ASC", "DESC", "OFFSET", "FETCH NEXT", "ROWS ONLY",
        "NULLS FIRST", "NULLS LAST"
    ];

    private static readonly string[] AfterInsert =
    [
        "VALUES", "SELECT", "DEFAULT VALUES", "OUTPUT", "EXEC", "EXECUTE"
    ];

    private static readonly string[] AfterUpdateSet =
    [
        "WHERE", "FROM", "OUTPUT"
    ];

    private static readonly string[] AfterWith =
    [
        "AS", "NOLOCK", "READUNCOMMITTED"
    ];

    private static readonly string[] GeneralKeywords =
    [
        "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE",
        "CREATE", "ALTER", "DROP",
        "EXEC", "EXECUTE",
        "BEGIN", "END", "IF", "ELSE", "WHILE",
        "DECLARE", "SET", "PRINT",
        "BEGIN TRANSACTION", "COMMIT", "ROLLBACK",
        "BEGIN TRY", "END TRY", "BEGIN CATCH", "END CATCH",
        "WITH", "GO", "USE"
    ];
}
