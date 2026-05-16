using System.Security.Cryptography;
using System.Text;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Orchestrates the analysis pipeline:
///   Parse document → split batches → hash → incremental skip → suppress → parallel rules → return.
///
/// Performance benchmarks (2026-03-22, AMD Ryzen 9, Release build):
///   1,000-line file  (~950 lines, 50 procedures, 50 batches):   ~52 ms  ✓ target &lt;200 ms
///   10,000-line file (~8,400 lines, 400 procedures, 400 batches): ~2,956 ms  ℹ target &lt;1,000 ms (soft)
///   Note: the 10,000-line result is dominated by TSqlParser batch parsing time (400 batches × ~7ms each).
///   Token-scan rules (ST001, ST006, ST008, DEP004, DEP007) are scoped to their batch offset range
///   to avoid O(batches × total_tokens) behaviour.
/// </summary>
public class AnalysisEngine(TsqlParserService parser, RuleRegistry registry, CaSettingsLoader settingsLoader)
{
    // Batch-level result cache: (sessionId + batchHash) → diagnostics
    private readonly Dictionary<string, List<AnalysisDiagnostic>> _batchCache = new();
    private readonly SemaphoreSlim _ruleSemaphore = new(8, 8);

    /// <summary>
    /// Analyzes the SQL document referenced by <paramref name="request"/> and returns all diagnostics.
    /// Uses batch-level result caching (keyed by SHA-256 hash) to skip unchanged batches.
    /// Rules run in parallel bounded by an internal semaphore (max 8 concurrent rules).
    /// </summary>
    /// <param name="serverVersion">
    /// The SQL Server major version (e.g. 16 for SQL Server 2022). Pass 0 to leave the
    /// parser's current setting untouched. The caller is responsible for resolving the
    /// session's server version (the engine's named-pipe path looks it up from
    /// <c>SessionManager</c>; the Blazor WASM web edition will pass a value from its
    /// browser-side session record).
    /// </param>
    /// <param name="schemaCache">
    /// The schema cache for the current session. Pass <see langword="null"/> to disable
    /// rules that require schema (<see cref="IAnalysisRule.RequiresSchema"/>). The named-pipe
    /// path looks this up from <c>SchemaCacheManager</c>; the web edition will source it
    /// from its IndexedDB-backed cache once T107 lands.
    /// </param>
    public async Task<CodeAnalysisResponse> AnalyzeAsync(
        CodeAnalysisRequest request,
        int serverVersion,
        DatabaseCache? schemaCache,
        CodeAnalysisSettings globalSettings,
        CancellationToken ct)
    {
        var settings = settingsLoader.Load(null, globalSettings);

        if (!settings.Enabled)
        {
            return new CodeAnalysisResponse
            {
                RequestId       = request.RequestId,
                AnalyzedVersion = request.DocumentVersion,
                Issues          = []
            };
        }

        // Ensure parser is using the correct server version for this session.
        // 0 = caller did not resolve; leave whatever was last set in place.
        if (serverVersion > 0)
            parser.SetServerVersion(serverVersion);

        // Parse full document once
        var script = parser.Parse(request.DocumentText, out var errors);

        if (script == null)
        {
            return new CodeAnalysisResponse
            {
                RequestId       = request.RequestId,
                AnalyzedVersion = request.DocumentVersion,
                Issues          = []
            };
        }

        var tokens = parser.GetTokenStream(request.DocumentText);

        // Parse suppressions once for the whole document
        var suppressions = SuppressionParser.Parse(tokens, out var metaDiagnostics);

        var allDiagnostics = new List<AnalysisDiagnostic>(metaDiagnostics);
        var enabledRules   = registry.GetEnabledRules(settings);

        foreach (var batch in script.Batches)
        {
            ct.ThrowIfCancellationRequested();

            var batchText = ExtractBatchText(request.DocumentText, batch);
            var batchHash = ComputeHash(request.SessionId + batchText);

            if (_batchCache.TryGetValue(batchHash, out var cached))
            {
                allDiagnostics.AddRange(cached);
                continue;
            }

            var ctx = new AnalysisContext
            {
                Script            = script,
                CurrentBatch      = batch,
                Tokens            = tokens,
                DocumentText      = request.DocumentText,
                SessionId         = request.SessionId,
                SchemaCache       = schemaCache,
                Settings          = settings,
                Suppressions      = suppressions,
                CancellationToken = ct
            };

            var batchDiagnostics = await RunRulesAsync(ctx, enabledRules, ct);
            _batchCache[batchHash] = batchDiagnostics;
            allDiagnostics.AddRange(batchDiagnostics);
        }

        // Apply suppression filter (inline noqa + global suppressions)
        var filtered = allDiagnostics
            .Where(d => !suppressions.IsSuppressed(d.Line, d.RuleId)
                     && !settings.GloballySuppressedRules.Contains(d.RuleId))
            .ToList();

        return new CodeAnalysisResponse
        {
            RequestId       = request.RequestId,
            AnalyzedVersion = request.DocumentVersion,
            Issues          = filtered.Select(ToIssueInfo).ToArray()
        };
    }

    private async Task<List<AnalysisDiagnostic>> RunRulesAsync(
        AnalysisContext ctx,
        IReadOnlyList<IAnalysisRule> rules,
        CancellationToken ct)
    {
        var results   = new List<AnalysisDiagnostic>[rules.Count];
        var tasks     = new Task[rules.Count];

        for (var i = 0; i < rules.Count; i++)
        {
            var rule  = rules[i];
            var index = i;

            if (rule.RequiresSchema && ctx.SchemaCache == null)
            {
                results[index] = [];
                tasks[index]   = Task.CompletedTask;
                continue;
            }

            tasks[index] = Task.Run(async () =>
            {
                await _ruleSemaphore.WaitAsync(ct);
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var diags = rule.Analyze(ctx).ToList();
                    results[index] = diags;
                }
                catch (OperationCanceledException)
                {
                    results[index] = [];
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Rule {RuleId} threw an exception", rule.RuleId);
                    results[index] = [];
                }
                finally
                {
                    _ruleSemaphore.Release();
                }
            }, ct);
        }

        await Task.WhenAll(tasks);

        var all = new List<AnalysisDiagnostic>();
        foreach (var r in results)
            if (r != null) all.AddRange(r);
        return all;
    }

    private static string ExtractBatchText(string documentText, TSqlBatch batch)
    {
        if (batch.Statements.Count == 0) return string.Empty;
        var first = batch.Statements[0];
        var last  = batch.Statements[^1];
        var start = first.StartOffset;
        var end   = last.StartOffset + last.FragmentLength;
        end = Math.Min(end, documentText.Length);
        return start < documentText.Length ? documentText[start..end] : string.Empty;
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static CodeIssueInfo ToIssueInfo(AnalysisDiagnostic d)
    {
        return new CodeIssueInfo
        {
            RuleId = d.RuleId,
            Severity = (int)d.Severity,
            Message = d.Message,
            StartOffset = d.StartOffset,
            EndOffset = d.EndOffset,
            Line = d.Line,
            Column = d.Column,
            FixActions = d.FixActions.Select(f => new FixActionInfo
            {
                Label = f.Label,
                FixType = (int)f.FixType,
                ReplacementStart = f.ReplacementStart,
                ReplacementEnd = f.ReplacementEnd,
                ReplacementText = f.ReplacementText,
                SuppressRuleId = f.SuppressRuleId,
                SuppressScopeCode = f.SuppressScope.HasValue ? (int?)f.SuppressScope : null
            }).ToArray()
        };
    }
}
