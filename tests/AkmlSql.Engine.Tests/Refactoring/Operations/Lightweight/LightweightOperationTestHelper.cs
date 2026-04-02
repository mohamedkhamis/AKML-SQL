using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring;
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
    /// </summary>
    public static RefactoringContext CreateContext(string sql)
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
            SchemaCache     = null
        };
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
