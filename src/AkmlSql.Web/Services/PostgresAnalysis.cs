using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (task T172, FR-151, FR-152, FR-155) - code analysis of a PostgreSQL script. The T-SQL rules run inside the browser; the PostgreSQL
/// rules live in the engine (they need the real PostgreSQL parser, which has native code), so a PostgreSQL script is sent there with the
/// session id (which gives the engine the connection's schema cache for the schema-aware rules) and the findings come back in the shape
/// the T-SQL analyser produces. Analysis is something the editor does as you type: when the engine cannot be asked, the answer is "no
/// findings", not an error.
/// </summary>
public interface IPostgresAnalysisEngine
{
    /// <summary>The findings of the PostgreSQL rules for a script; an empty response when no engine can be asked.</summary>
    Task<CodeAnalysisResponse> AnalyseAsync(string documentText, CancellationToken ct);

    /// <summary>The PostgreSQL rule set with each rule's state under the engine's settings; empty when no engine can be asked.</summary>
    Task<IReadOnlyList<AnalysisRuleInfoDto>> ListRulesAsync(CancellationToken ct);
}

internal sealed class EnginePostgresAnalysis : IPostgresAnalysisEngine
{
    private readonly IEngineBridge _bridge;
    private readonly ISqlConnectionService _connection;

    public EnginePostgresAnalysis(IEngineBridge bridge, ISqlConnectionService connection)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    private bool Available => _bridge.State == BridgeState.Open && _bridge.Advertises(SqlConnectionService.CapabilityPostgreSql);

    public async Task<CodeAnalysisResponse> AnalyseAsync(string documentText, CancellationToken ct)
    {
        var none = new CodeAnalysisResponse { RequestId = Guid.NewGuid().ToString(), Issues = Array.Empty<CodeIssueInfo>(), AnalyzedVersion = 1 };
        if (!Available) return none;

        try
        {
            var response = await _bridge.SendAsync<CodeAnalysisRequest, CodeAnalysisResponse>(
                MessageTypes.RequestAnalyze,
                new CodeAnalysisRequest
                {
                    SessionId = _connection.SessionId,
                    RequestId = none.RequestId,
                    DocumentText = documentText ?? string.Empty,
                    DocumentVersion = 1,
                },
                ct).ConfigureAwait(false);
            if (response == null) return none;
            response.Issues ??= Array.Empty<CodeIssueInfo>();
            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return none;       // the engine went away mid-request; the next pass asks again
        }
    }

    public async Task<IReadOnlyList<AnalysisRuleInfoDto>> ListRulesAsync(CancellationToken ct)
    {
        if (!Available) return Array.Empty<AnalysisRuleInfoDto>();

        try
        {
            var response = await _bridge.SendAsync<ListAnalysisRulesRequest, ListAnalysisRulesResponse>(
                MessageTypes.ListAnalysisRules,
                new ListAnalysisRulesRequest { IncludePostgreSql = true },
                ct).ConfigureAwait(false);
            if (response is not { Success: true } || response.Rules == null) return Array.Empty<AnalysisRuleInfoDto>();

            return response.Rules.Where(r => r.Kind == (int)AkmlSql.Core.DatabaseKind.PostgreSql).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<AnalysisRuleInfoDto>();
        }
    }
}
