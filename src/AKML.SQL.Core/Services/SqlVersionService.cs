using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for SQL Server version management and feature detection.
/// Implements Story 11.2: Multi-Version Support.
/// </summary>
public class SqlVersionService : ISqlVersionService
{
    private readonly ILogger<SqlVersionService> _logger;
    private readonly Dictionary<SqlServerVersion, VersionFeatures> _featuresByVersion;

    public SqlVersionService(ILogger<SqlVersionService> logger)
    {
        _logger = logger;
        _featuresByVersion = InitializeFeatures();
    }

    /// <summary>
    /// Gets the parser for a specific SQL Server version.
    /// </summary>
    public TSqlParser GetParser(SqlServerVersion version)
    {
        return version switch
        {
            SqlServerVersion.Sql2008 => new TSql100Parser(initialQuotedIdentifiers: true),
            SqlServerVersion.Sql2012 => new TSql110Parser(initialQuotedIdentifiers: true),
            SqlServerVersion.Sql2014 => new TSql120Parser(initialQuotedIdentifiers: true),
            SqlServerVersion.Sql2016 => new TSql130Parser(initialQuotedIdentifiers: true),
            SqlServerVersion.Sql2017 => new TSql140Parser(initialQuotedIdentifiers: true),
            SqlServerVersion.Sql2019 => new TSql150Parser(initialQuotedIdentifiers: true),
            SqlServerVersion.Sql2022 => new TSql160Parser(initialQuotedIdentifiers: true),
            _ => new TSql160Parser(initialQuotedIdentifiers: true)
        };
    }

    /// <summary>
    /// Gets the script generator for a specific SQL Server version.
    /// </summary>
    public SqlScriptGenerator GetScriptGenerator(SqlServerVersion version)
    {
        return version switch
        {
            SqlServerVersion.Sql2008 => new Sql100ScriptGenerator(),
            SqlServerVersion.Sql2012 => new Sql110ScriptGenerator(),
            SqlServerVersion.Sql2014 => new Sql120ScriptGenerator(),
            SqlServerVersion.Sql2016 => new Sql130ScriptGenerator(),
            SqlServerVersion.Sql2017 => new Sql140ScriptGenerator(),
            SqlServerVersion.Sql2019 => new Sql150ScriptGenerator(),
            SqlServerVersion.Sql2022 => new Sql160ScriptGenerator(),
            _ => new Sql160ScriptGenerator()
        };
    }

    /// <summary>
    /// Detects the SQL Server version from a version string.
    /// </summary>
    public SqlServerVersion DetectVersion(string versionString)
    {
        if (string.IsNullOrEmpty(versionString))
            return SqlServerVersion.Latest;

        // Parse major version number
        var parts = versionString.Split('.');
        if (parts.Length > 0 && int.TryParse(parts[0], out var major))
        {
            return major switch
            {
                10 => SqlServerVersion.Sql2008,
                11 => SqlServerVersion.Sql2012,
                12 => SqlServerVersion.Sql2014,
                13 => SqlServerVersion.Sql2016,
                14 => SqlServerVersion.Sql2017,
                15 => SqlServerVersion.Sql2019,
                >= 16 => SqlServerVersion.Sql2022,
                _ => SqlServerVersion.Latest
            };
        }

        return SqlServerVersion.Latest;
    }

    /// <summary>
    /// Detects the SQL Server version from a product version number.
    /// </summary>
    public SqlServerVersion DetectVersionFromProductVersion(int majorVersion)
    {
        return majorVersion switch
        {
            10 => SqlServerVersion.Sql2008,
            11 => SqlServerVersion.Sql2012,
            12 => SqlServerVersion.Sql2014,
            13 => SqlServerVersion.Sql2016,
            14 => SqlServerVersion.Sql2017,
            15 => SqlServerVersion.Sql2019,
            >= 16 => SqlServerVersion.Sql2022,
            _ => SqlServerVersion.Latest
        };
    }

    /// <summary>
    /// Gets the features available in a specific version.
    /// </summary>
    public VersionFeatures GetFeatures(SqlServerVersion version)
    {
        return _featuresByVersion.TryGetValue(version, out var features)
            ? features
            : _featuresByVersion[SqlServerVersion.Latest];
    }

    /// <summary>
    /// Checks if a feature is available in a specific version.
    /// </summary>
    public bool IsFeatureAvailable(SqlServerVersion version, SqlFeature feature)
    {
        var features = GetFeatures(version);
        return features.AvailableFeatures.Contains(feature);
    }

    /// <summary>
    /// Gets the display name for a version.
    /// </summary>
    public string GetVersionDisplayName(SqlServerVersion version)
    {
        return version switch
        {
            SqlServerVersion.Sql2008 => "SQL Server 2008",
            SqlServerVersion.Sql2012 => "SQL Server 2012",
            SqlServerVersion.Sql2014 => "SQL Server 2014",
            SqlServerVersion.Sql2016 => "SQL Server 2016",
            SqlServerVersion.Sql2017 => "SQL Server 2017",
            SqlServerVersion.Sql2019 => "SQL Server 2019",
            SqlServerVersion.Sql2022 => "SQL Server 2022",
            SqlServerVersion.Latest => "SQL Server (Latest)",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Gets all supported versions.
    /// </summary>
    public IReadOnlyList<SqlServerVersion> GetSupportedVersions()
    {
        return Enum.GetValues<SqlServerVersion>()
            .Where(v => v != SqlServerVersion.Latest)
            .OrderByDescending(v => (int)v)
            .ToList();
    }

    /// <summary>
    /// Gets keywords introduced in a specific version.
    /// </summary>
    public IReadOnlyList<string> GetVersionKeywords(SqlServerVersion version)
    {
        return GetFeatures(version).Keywords;
    }

    /// <summary>
    /// Gets functions introduced in a specific version.
    /// </summary>
    public IReadOnlyList<string> GetVersionFunctions(SqlServerVersion version)
    {
        return GetFeatures(version).Functions;
    }

    /// <summary>
    /// Validates SQL against a specific version.
    /// </summary>
    public VersionValidationResult ValidateForVersion(string sql, SqlServerVersion version)
    {
        var result = new VersionValidationResult { Version = version, IsValid = true };

        if (string.IsNullOrWhiteSpace(sql))
            return result;

        var parser = GetParser(version);
        using var reader = new StringReader(sql);
        parser.Parse(reader, out var errors);

        if (errors != null && errors.Count > 0)
        {
            result.IsValid = false;
            result.Errors = errors.Select(e => new VersionValidationError
            {
                Message = e.Message,
                Line = e.Line,
                Column = e.Column
            }).ToList();
        }

        return result;
    }

    private Dictionary<SqlServerVersion, VersionFeatures> InitializeFeatures()
    {
        return new Dictionary<SqlServerVersion, VersionFeatures>
        {
            [SqlServerVersion.Sql2008] = new VersionFeatures
            {
                Version = SqlServerVersion.Sql2008,
                MajorVersion = 10,
                AvailableFeatures = new HashSet<SqlFeature>
                {
                    SqlFeature.CommonTableExpressions,
                    SqlFeature.MergeStatement,
                    SqlFeature.CompressedBackups
                },
                Keywords = new List<string> { "MERGE", "OUTPUT" },
                Functions = new List<string> { "ROW_NUMBER", "RANK", "DENSE_RANK" }
            },
            [SqlServerVersion.Sql2012] = new VersionFeatures
            {
                Version = SqlServerVersion.Sql2012,
                MajorVersion = 11,
                AvailableFeatures = new HashSet<SqlFeature>
                {
                    SqlFeature.CommonTableExpressions,
                    SqlFeature.MergeStatement,
                    SqlFeature.CompressedBackups,
                    SqlFeature.WindowFunctions,
                    SqlFeature.SequenceObjects,
                    SqlFeature.OffsetFetch,
                    SqlFeature.ThrowStatement
                },
                Keywords = new List<string> { "THROW", "SEQUENCE", "OFFSET", "FETCH" },
                Functions = new List<string> { "TRY_CONVERT", "FORMAT", "IIF", "CHOOSE", "CONCAT", "LAG", "LEAD" }
            },
            [SqlServerVersion.Sql2014] = new VersionFeatures
            {
                Version = SqlServerVersion.Sql2014,
                MajorVersion = 12,
                AvailableFeatures = new HashSet<SqlFeature>
                {
                    SqlFeature.CommonTableExpressions,
                    SqlFeature.MergeStatement,
                    SqlFeature.WindowFunctions,
                    SqlFeature.SequenceObjects,
                    SqlFeature.OffsetFetch,
                    SqlFeature.ThrowStatement,
                    SqlFeature.InMemoryOltp,
                    SqlFeature.NativelyCompiledProcedures
                },
                Keywords = new List<string> { "MEMORY_OPTIMIZED", "NATIVE_COMPILATION" },
                Functions = new List<string> { "TRY_PARSE", "PARSE" }
            },
            [SqlServerVersion.Sql2016] = new VersionFeatures
            {
                Version = SqlServerVersion.Sql2016,
                MajorVersion = 13,
                AvailableFeatures = new HashSet<SqlFeature>
                {
                    SqlFeature.CommonTableExpressions,
                    SqlFeature.MergeStatement,
                    SqlFeature.WindowFunctions,
                    SqlFeature.SequenceObjects,
                    SqlFeature.OffsetFetch,
                    SqlFeature.ThrowStatement,
                    SqlFeature.InMemoryOltp,
                    SqlFeature.JsonSupport,
                    SqlFeature.TemporalTables,
                    SqlFeature.RowLevelSecurity,
                    SqlFeature.AlwaysEncrypted,
                    SqlFeature.DropIfExists
                },
                Keywords = new List<string> { "JSON", "FOR JSON", "OPENJSON", "DROP IF EXISTS" },
                Functions = new List<string> { "JSON_VALUE", "JSON_QUERY", "JSON_MODIFY", "STRING_SPLIT", "STRING_AGG" }
            },
            [SqlServerVersion.Sql2017] = new VersionFeatures
            {
                Version = SqlServerVersion.Sql2017,
                MajorVersion = 14,
                AvailableFeatures = new HashSet<SqlFeature>
                {
                    SqlFeature.CommonTableExpressions,
                    SqlFeature.MergeStatement,
                    SqlFeature.WindowFunctions,
                    SqlFeature.SequenceObjects,
                    SqlFeature.OffsetFetch,
                    SqlFeature.ThrowStatement,
                    SqlFeature.InMemoryOltp,
                    SqlFeature.JsonSupport,
                    SqlFeature.TemporalTables,
                    SqlFeature.RowLevelSecurity,
                    SqlFeature.AlwaysEncrypted,
                    SqlFeature.DropIfExists,
                    SqlFeature.GraphDatabase,
                    SqlFeature.ResumableIndexOperations
                },
                Keywords = new List<string> { "GRAPH", "MATCH", "WITHIN GROUP" },
                Functions = new List<string> { "TRIM", "CONCAT_WS", "TRANSLATE", "STRING_AGG" }
            },
            [SqlServerVersion.Sql2019] = new VersionFeatures
            {
                Version = SqlServerVersion.Sql2019,
                MajorVersion = 15,
                AvailableFeatures = new HashSet<SqlFeature>
                {
                    SqlFeature.CommonTableExpressions,
                    SqlFeature.MergeStatement,
                    SqlFeature.WindowFunctions,
                    SqlFeature.SequenceObjects,
                    SqlFeature.OffsetFetch,
                    SqlFeature.ThrowStatement,
                    SqlFeature.InMemoryOltp,
                    SqlFeature.JsonSupport,
                    SqlFeature.TemporalTables,
                    SqlFeature.RowLevelSecurity,
                    SqlFeature.AlwaysEncrypted,
                    SqlFeature.DropIfExists,
                    SqlFeature.GraphDatabase,
                    SqlFeature.ResumableIndexOperations,
                    SqlFeature.AcceleratedDatabaseRecovery,
                    SqlFeature.Utf8Support,
                    SqlFeature.ApproxCountDistinct
                },
                Keywords = new List<string> { "ACCELERATED_DATABASE_RECOVERY", "UTF8" },
                Functions = new List<string> { "APPROX_COUNT_DISTINCT", "GREATEST", "LEAST" }
            },
            [SqlServerVersion.Sql2022] = new VersionFeatures
            {
                Version = SqlServerVersion.Sql2022,
                MajorVersion = 16,
                AvailableFeatures = new HashSet<SqlFeature>
                {
                    SqlFeature.CommonTableExpressions,
                    SqlFeature.MergeStatement,
                    SqlFeature.WindowFunctions,
                    SqlFeature.SequenceObjects,
                    SqlFeature.OffsetFetch,
                    SqlFeature.ThrowStatement,
                    SqlFeature.InMemoryOltp,
                    SqlFeature.JsonSupport,
                    SqlFeature.TemporalTables,
                    SqlFeature.RowLevelSecurity,
                    SqlFeature.AlwaysEncrypted,
                    SqlFeature.DropIfExists,
                    SqlFeature.GraphDatabase,
                    SqlFeature.ResumableIndexOperations,
                    SqlFeature.AcceleratedDatabaseRecovery,
                    SqlFeature.Utf8Support,
                    SqlFeature.ApproxCountDistinct,
                    SqlFeature.LedgerTables,
                    SqlFeature.WindowFrameExclusion,
                    SqlFeature.DateTimeBucket,
                    SqlFeature.IsDistinctFrom
                },
                Keywords = new List<string> { "LEDGER", "IS DISTINCT FROM", "WINDOW" },
                Functions = new List<string> { "DATE_BUCKET", "DATETRUNC", "GENERATE_SERIES", "FIRST_VALUE", "LAST_VALUE" }
            },
            [SqlServerVersion.Latest] = new VersionFeatures
            {
                Version = SqlServerVersion.Latest,
                MajorVersion = 16,
                AvailableFeatures = new HashSet<SqlFeature>(Enum.GetValues<SqlFeature>()),
                Keywords = new List<string>(),
                Functions = new List<string>()
            }
        };
    }
}

/// <summary>
/// Interface for SQL version service.
/// </summary>
public interface ISqlVersionService
{
    TSqlParser GetParser(SqlServerVersion version);
    SqlScriptGenerator GetScriptGenerator(SqlServerVersion version);
    SqlServerVersion DetectVersion(string versionString);
    SqlServerVersion DetectVersionFromProductVersion(int majorVersion);
    VersionFeatures GetFeatures(SqlServerVersion version);
    bool IsFeatureAvailable(SqlServerVersion version, SqlFeature feature);
    string GetVersionDisplayName(SqlServerVersion version);
    IReadOnlyList<SqlServerVersion> GetSupportedVersions();
    IReadOnlyList<string> GetVersionKeywords(SqlServerVersion version);
    IReadOnlyList<string> GetVersionFunctions(SqlServerVersion version);
    VersionValidationResult ValidateForVersion(string sql, SqlServerVersion version);
}

/// <summary>
/// SQL Server versions supported.
/// </summary>
public enum SqlServerVersion
{
    Sql2008 = 10,
    Sql2012 = 11,
    Sql2014 = 12,
    Sql2016 = 13,
    Sql2017 = 14,
    Sql2019 = 15,
    Sql2022 = 16,
    Latest = 100
}

/// <summary>
/// SQL Server features.
/// </summary>
public enum SqlFeature
{
    CommonTableExpressions,
    MergeStatement,
    CompressedBackups,
    WindowFunctions,
    SequenceObjects,
    OffsetFetch,
    ThrowStatement,
    InMemoryOltp,
    NativelyCompiledProcedures,
    JsonSupport,
    TemporalTables,
    RowLevelSecurity,
    AlwaysEncrypted,
    DropIfExists,
    GraphDatabase,
    ResumableIndexOperations,
    AcceleratedDatabaseRecovery,
    Utf8Support,
    ApproxCountDistinct,
    LedgerTables,
    WindowFrameExclusion,
    DateTimeBucket,
    IsDistinctFrom
}

/// <summary>
/// Features available in a specific version.
/// </summary>
public class VersionFeatures
{
    public SqlServerVersion Version { get; set; }
    public int MajorVersion { get; set; }
    public HashSet<SqlFeature> AvailableFeatures { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<string> Functions { get; set; } = new();
}

/// <summary>
/// Result of version validation.
/// </summary>
public class VersionValidationResult
{
    public SqlServerVersion Version { get; set; }
    public bool IsValid { get; set; }
    public List<VersionValidationError> Errors { get; set; } = new();
}

/// <summary>
/// Version validation error.
/// </summary>
public class VersionValidationError
{
    public string Message { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
}
