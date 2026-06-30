using AkmlSql.Core.Config;
using AkmlSql.Engine.Schema;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring;

/// <summary>
/// Holds all per-request inputs needed by a refactoring operation:
/// the parsed AST, token stream, selection bounds, session ID, settings,
/// schema cache, and any additional file paths for cross-file scope.
/// </summary>
public class RefactoringContext
{
    public TSqlScript Script           { get; set; } = null!;
    public IList<TSqlParserToken> Tokens { get; set; } = null!;
    public string DocumentText         { get; set; } = string.Empty;
    public string DocumentPath         { get; set; } = string.Empty;
    public int SelectionStart          { get; set; }
    public int SelectionLength         { get; set; }
    public string SessionId            { get; set; } = string.Empty;
    public RefactoringSettings Settings { get; set; } = new();
    public DatabaseCache? SchemaCache  { get; set; }
    public string[] AdditionalFilePaths { get; set; } = [];

    /// <summary>
    /// The active session's connection string, when connected. Heavyweight operations that need a
    /// live catalog lookup (e.g. Inline Stored Procedure fetching the body from sys.sql_modules)
    /// read it here; it is only consulted during Preview. Null when there is no live connection —
    /// such operations then return CanApply = false.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// IntelliSense policy flags consulted by lightweight refactoring operations
    /// (e.g. <c>InsertOptions.IncludeColumns</c> gates ExpandInsertColumns).
    /// When null, operations fall back to <c>ConfigManager.Load().IntelliSense</c>.
    /// Tests inject explicit values here to avoid disk I/O.
    /// </summary>
    public IntelliSenseSettings? IntelliSense { get; set; }

    /// <summary>True if the request includes a non-empty text selection.</summary>
    public bool HasSelection => SelectionLength > 0;
}
