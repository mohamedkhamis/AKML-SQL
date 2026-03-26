using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
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
public class AnalysisEngine
{
    private readonly TsqlParserService _parser;
    private readonly RuleRegistry _registry;
    private readonly CaSettingsLoader _settingsLoader;

    // Batch-level result cache: (sessionId + batchHash) → diagnostics
    private readonly Dictionary<string, List<AnalysisDiagnostic>> _batchCache = new();
    private readonly SemaphoreSlim _ruleSemaphore = new(8, 8);

    public AnalysisEngine(TsqlParserService parser, RuleRegistry registry, CaSettingsLoader settingsLoader)
    {
        _parser         = parser;
        _registry       = registry;
        _settingsLoader = settingsLoader;
    }

    public async Task<CodeAnalysisResponse> AnalyzeAsync(
        CodeAnalysisRequest request,
        SessionManager sessionManager,
        SchemaCacheManager schemaCacheManager,
        CodeAnalysisSettings globalSettings,
        CancellationToken ct)
    {
        var settings = _settingsLoader.Load(null, globalSettings);

        if (!settings.Enabled)
        {
            return new CodeAnalysisResponse
            {
                RequestId       = request.RequestId,
                AnalyzedVersion = request.DocumentVersion,
                Issues          = []
            };
        }

        var session    = sessionManager.GetSession(request.SessionId);
        var schemaCache = session != null
            ? schemaCacheManager.GetOrCreateCache(session.SessionId, session.DatabaseName)
            : null;

        // Ensure parser is using the correct server version for this session
        if (session != null)
            _parser.SetServerVersion(session.ServerVersion);

        // Parse full document once
        var script = _parser.Parse(request.DocumentText, out var errors);

        if (script == null)
        {
            return new CodeAnalysisResponse
            {
                RequestId       = request.RequestId,
                AnalyzedVersion = request.DocumentVersion,
                Issues          = []
            };
        }

        var tokens = _parser.GetTokenStream(request.DocumentText);

        // Parse suppressions once for the whole document
        var suppressions = SuppressionParser.Parse(tokens, out var metaDiagnostics);

        var allDiagnostics = new List<AnalysisDiagnostic>(metaDiagnostics);
        var enabledRules   = _registry.GetEnabledRules(settings);

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

    private static CodeIssueInfo ToIssueInfo(AnalysisDiagnostic d) => new()
    {
        RuleId      = d.RuleId,
        Severity    = (int)d.Severity,
        Message     = d.Message,
        StartOffset = d.StartOffset,
        EndOffset   = d.EndOffset,
        Line        = d.Line,
        Column      = d.Column,
        FixActions  = d.FixActions.Select(f => new FixActionInfo
        {
            Label            = f.Label,
            FixType          = (int)f.FixType,
            ReplacementStart = f.ReplacementStart,
            ReplacementEnd   = f.ReplacementEnd,
            ReplacementText  = f.ReplacementText,
            SuppressRuleId   = f.SuppressRuleId,
            SuppressScopeCode = f.SuppressScope.HasValue ? (int?)f.SuppressScope : null
        }).ToArray()
    };
}
