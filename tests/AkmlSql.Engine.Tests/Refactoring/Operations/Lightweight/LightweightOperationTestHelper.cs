using AkmlSql.Core.Config;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Schema;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Lightweight;

/// <summary>
/// Helper for creating a minimal RefactoringContext for unit tests.
/// Uses TSql160Parser, no schema cache, no session.
/// </summary>
internal static class LightweightOperationTestHelper
{
    private static readonly TsqlParserService ParserService;

    static LightweightOperationTestHelper()
    {
        ParserService = new TsqlParserService();
        ParserService.SetServerVersion(160);
    }

    /// <summary>
    /// Creates a RefactoringContext with the given SQL, a parsed AST and token stream.
    /// No schema cache. No selection.
    /// Pass <paramref name="intelliSense"/> to inject explicit policy flags
    /// (e.g. <c>InsertOptions.IncludeColumns = false</c>); when null, operations
    /// fall back to <c>ConfigManager.Load()</c>.
    /// </summary>
    public static RefactoringContext CreateContext(string sql, IntelliSenseSettings? intelliSense = null)
    {
        var script = ParserService.Parse(sql, out _)
                     ?? new TSqlScript();
        var tokens = ParserService.GetTokenStream(sql);

        return new RefactoringContext
        {
            DocumentText    = sql,
            Script          = script,
            Tokens          = tokens,
            SelectionStart  = 0,
            SelectionLength = 0,
            SessionId       = "test",
            SchemaCache     = null,
            IntelliSense    = intelliSense
        };
    }

    /// <summary>
    /// Creates a RefactoringContext backed by an in-memory <see cref="DatabaseCache"/>.
    /// Mirrors the null-cache <see cref="CreateContext(string, IntelliSenseSettings?)"/> overload but
    /// wires <c>ctx.SchemaCache</c> so schema-aware ops (ExpandWildcards / QualifyObjectNames) resolve.
    /// </summary>
    public static RefactoringContext CreateContext(string sql, DatabaseCache cache)
    {
        var ctx = CreateContext(sql);
        ctx.SchemaCache = cache;
        return ctx;
    }

    /// <summary>
    /// Creates a RefactoringContext with an active text selection.
    /// </summary>
    public static RefactoringContext CreateContextWithSelection(
        string sql, int selectionStart, int selectionLength)
    {
        var ctx = CreateContext(sql);
        ctx.SelectionStart  = selectionStart;
        ctx.SelectionLength = selectionLength;
        return ctx;
    }
}
