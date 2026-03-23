using System.Collections.Generic;
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

    /// <summary>True if the request includes a non-empty text selection.</summary>
    public bool HasSelection => SelectionLength > 0;
}
