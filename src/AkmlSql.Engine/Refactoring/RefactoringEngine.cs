using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using Serilog;

namespace AkmlSql.Engine.Refactoring;

/// <summary>
/// Orchestrates heavyweight refactoring operations.
/// Dispatches preview and apply requests to the appropriate operation handler
/// based on <see cref="RefactorOperationType"/>.
/// </summary>
public class RefactoringEngine(TsqlParserService parser, SchemaCacheManager schemaCacheManager)
{
    public async Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        SessionManager sessionManager,
        CancellationToken ct)
    {
        try
        {
            var ctx = BuildContext(request, sessionManager);
            return (RefactorOperationType)request.OperationType switch
            {
                RefactorOperationType.SafeRename =>
                    await new SafeRenameOperation().PreviewAsync(request, ctx, ct),
                RefactorOperationType.ExtractToCte =>
                    await new ExtractToCteOperation().PreviewAsync(request, ctx, ct),
                RefactorOperationType.ExtractToProc =>
                    await new ExtractToProcOperation().PreviewAsync(request, ctx, ct),
                RefactorOperationType.ExtractToDerivedTable =>
                    await new ExtractToDerivedTableOperation().PreviewAsync(request, ctx, ct),
                RefactorOperationType.EncapsulateAsView =>
                    await new EncapsulateAsViewOperation().PreviewAsync(request, ctx, ct),
                RefactorOperationType.ConvertTempToTableVar or
                RefactorOperationType.ConvertTableVarToTemp =>
                    await new ConvertTempTableOperation().PreviewAsync(request, ctx, ct),
                RefactorOperationType.ParameterizeValues =>
                    await new ParameterizeValuesOperation().PreviewAsync(request, ctx, ct),
                RefactorOperationType.SplitTable =>
                    await new SplitTableOperation().PreviewAsync(request, ctx, ct),
                _ => new RefactorPreviewResponse
                {
                    CanApply = false,
                    Errors = [$"Unknown operation type: {request.OperationType}"]
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RefactoringEngine.PreviewAsync failed for OperationType={Op}", request.OperationType);
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors = [$"Preview failed: {ex.Message}"]
            };
        }
    }

    public async Task<RefactorApplyResponse> ApplyAsync(
        RefactorApplyRequest request,
        CancellationToken ct)
    {
        try
        {
            return (RefactorOperationType)request.OperationType switch
            {
                RefactorOperationType.SafeRename =>
                    await new SafeRenameOperation().ApplyAsync(request, ct),
                RefactorOperationType.ExtractToCte =>
                    await new ExtractToCteOperation().ApplyAsync(request, ct),
                RefactorOperationType.ExtractToProc =>
                    await new ExtractToProcOperation().ApplyAsync(request, ct),
                RefactorOperationType.ExtractToDerivedTable =>
                    await new ExtractToDerivedTableOperation().ApplyAsync(request, ct),
                RefactorOperationType.EncapsulateAsView =>
                    await new EncapsulateAsViewOperation().ApplyAsync(request, ct),
                RefactorOperationType.ConvertTempToTableVar or
                RefactorOperationType.ConvertTableVarToTemp =>
                    await new ConvertTempTableOperation().ApplyAsync(request, ct),
                RefactorOperationType.ParameterizeValues =>
                    await new ParameterizeValuesOperation().ApplyAsync(request, ct),
                RefactorOperationType.SplitTable =>
                    await new SplitTableOperation().ApplyAsync(request, ct),
                _ => new RefactorApplyResponse { Success = false }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RefactoringEngine.ApplyAsync failed for OperationType={Op}", request.OperationType);
            return new RefactorApplyResponse { Success = false };
        }
    }

    private RefactoringContext BuildContext(RefactorPreviewRequest request, SessionManager sessionManager)
    {
        var session = sessionManager.GetSession(request.SessionId);
        var cache = session != null
            ? schemaCacheManager.GetCache(request.SessionId, session.DatabaseName)
            : null;

        var script = parser.Parse(request.DocumentText, out _) ?? new Microsoft.SqlServer.TransactSql.ScriptDom.TSqlScript();
        var tokens = parser.GetTokenStream(request.DocumentText);
        var settings = Core.Config.ConfigManager.Load().Refactoring;

        return new RefactoringContext
        {
            Script            = script,
            Tokens            = tokens,
            DocumentText      = request.DocumentText,
            DocumentPath      = request.DocumentPath,
            SelectionStart    = request.SelectionStart,
            SelectionLength   = request.SelectionLength,
            SessionId         = request.SessionId,
            Settings          = settings,
            SchemaCache       = cache,
            AdditionalFilePaths = request.AdditionalFilePaths
        };
    }
}
