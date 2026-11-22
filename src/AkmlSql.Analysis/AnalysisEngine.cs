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
/// <param name="sessionSuppressions">
/// Rules switched off for the current session (see <see cref="SessionSuppressionStore"/>).
/// Optional so existing callers — the CLI analyzer and the web edition, which have no session
/// concept — keep compiling and behave exactly as before.
/// </param>
public class AnalysisEngine(
    TsqlParserService parser,
    RuleRegistry registry,
    CaSettingsLoader settingsLoader,
    SessionSuppressionStore? sessionSuppressions = null)
{
    // Batch-level result cache: (sessionId + batchHash) → diagnostics
    private readonly Dictionary<string, List<AnalysisDiagnostic>> _batchCache = new();
    private readonly SemaphoreSlim _ruleSemaphore = new(8, 8);

    /// <summary>
    /// Clears the batch-level result cache so the next analysis re-runs all rules under the
    /// current settings. Must be called whenever analysis settings change (rule enable/disable,
    /// severity overrides) to prevent stale diagnostics being returned for unchanged batches.
    /// Thread-safe: takes a lock matching the cache's single-writer usage pattern.
    /// </summary>
    public void ClearBatchCache()
    {
        lock (_batchCache)
        {
            _batchCache.Clear();
        }
    }

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
    /// <param name="reportParseErrors">
    /// Spec 047 (WEB-005, contracts/analysis-display.md §4): also report every parse error as a <c>SYNTAX</c> Error
    /// finding, first, whether or not a partial tree exists. The web edition asks for it (its Problems tab said "No
    /// problems found." for SQL that does not parse); the SSMS engine handler and the CLI keep the default, because SSMS
    /// underlines syntax errors itself.
    /// </param>
    public async Task<CodeAnalysisResponse> AnalyzeAsync(
        CodeAnalysisRequest request,
        int serverVersion,
        DatabaseCache? schemaCache,
        CodeAnalysisSettings globalSettings,
        CancellationToken ct,
        bool reportParseErrors = false)
    {
        // Spec 030 (T049/T051): resolve the document's directory so per-project .casettings (rule
        // enable/severity + global suppressions) apply in the LIVE editor, matching the CLI. An empty
        // FilePath (e.g. an unsaved buffer) preserves the previous null ⇒ global-defaults behaviour.
        var caDir = string.IsNullOrEmpty(request.FilePath)
            ? null
            : System.IO.Path.GetDirectoryName(request.FilePath);
        var settings = settingsLoader.Load(caDir, globalSettings);

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
        var tokens = parser.GetTokenStream(request.DocumentText);

        // Spec 047 (WEB-005): never cached per batch, never suppressed — they describe the text as it is now.
        var syntaxIssues = reportParseErrors ? SyntaxIssues(errors, tokens) : [];

        if (script == null)
        {
            return new CodeAnalysisResponse
            {
                RequestId       = request.RequestId,
                AnalyzedVersion = request.DocumentVersion,
                Issues          = syntaxIssues
            };
        }

        // Parse suppressions once for the whole document
        var suppressions = SuppressionParser.Parse(tokens, out var metaDiagnostics);

        var allDiagnostics = new List<AnalysisDiagnostic>(metaDiagnostics);
        var enabledRules   = registry.GetEnabledRules(settings);

        foreach (var batch in script.Batches)
        {
            ct.ThrowIfCancellationRequested();

            var batchText = ExtractBatchText(request.DocumentText, batch);
            var batchHash = ComputeHash(request.SessionId + batchText);

            // Guarded: ClearBatchCache can fire on the notification thread at any moment, and an
            // unsynchronised Dictionary read racing a Clear can throw or return a torn bucket.
            List<AnalysisDiagnostic>? cached;
            lock (_batchCache) { _batchCache.TryGetValue(batchHash, out cached); }

            if (cached != null)
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
            lock (_batchCache) { _batchCache[batchHash] = batchDiagnostics; }
            allDiagnostics.AddRange(batchDiagnostics);
        }

        // Apply the suppression filter: inline directives, .casettings globalSuppressions, and the
        // rules switched off for this session. Session suppressions are filtered here rather than
        // by dropping the rule from enabledRules so that cached batches stay valid and lifting a
        // suppression shows the findings again on the very next pass.
        var filtered = allDiagnostics
            .Where(d => !suppressions.IsSuppressed(d.Line, d.RuleId)
                     && !settings.GloballySuppressedRules.Contains(d.RuleId)
                     && sessionSuppressions?.IsSuppressed(d.RuleId) != true)
            .ToList();

        return new CodeAnalysisResponse
        {
            RequestId       = request.RequestId,
            AnalyzedVersion = request.DocumentVersion,
            Issues          = syntaxIssues.Concat(filtered.Select(ToIssueInfo)).ToArray()
        };
    }

    /// <summary>
    /// Spec 047 (WEB-005): one <c>SYNTAX</c> Error finding per parse error, at the parser's line, column and message,
    /// spanning the token the parser stopped at when there is one (so the editor underlines that token, else the line).
    /// </summary>
    private static CodeIssueInfo[] SyntaxIssues(IList<ParseError>? errors, IList<TSqlParserToken>? tokens)
    {
        if (errors == null || errors.Count == 0) return [];
        var issues = new List<CodeIssueInfo>(errors.Count);
        foreach (var e in errors)
        {
            // "GO 3" runs a batch three times in SSMS and sqlcmd; ScriptDom alone calls the count a syntax error.
            if (tokens != null && TsqlParserService.IsGoRepeatCount(e, tokens)) continue;
            var token = tokens?.FirstOrDefault(t => t.Offset == e.Offset && t.TokenType != TSqlTokenType.EndOfFile);
            issues.Add(new CodeIssueInfo
            {
                RuleId      = SyntaxRuleId,
                Severity    = 3,
                Message     = e.Message,
                StartOffset = e.Offset,
                EndOffset   = e.Offset + (token?.Text?.Length ?? 0),
                Line        = e.Line,
                Column      = e.Column,
                Description = "The script does not parse: SQL Server would reject it before running anything.",
            });
        }
        return issues.ToArray();
    }

    /// <summary>Spec 047 (WEB-005): the rule id of a parse error reported as a finding.</summary>
    public const string SyntaxRuleId = "SYNTAX";

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
        // Spec 030 T055 (FR-028) — attach the rule's catalog description + reference URL per-issue so
        // the shell's Ctrl-hover issue-details popup can render them (the catalog is engine-only).
        var meta = RuleMetadataCatalog.Get(d.RuleId);
        return new CodeIssueInfo
        {
            RuleId = d.RuleId,
            Severity = (int)d.Severity,
            Message = d.Message,
            StartOffset = d.StartOffset,
            EndOffset = d.EndOffset,
            Line = d.Line,
            Column = d.Column,
            Description = meta.Description,
            ReferenceUrl = meta.ReferenceUrl,
            // Spec 030 T054 (FR-027) — carry the rule's auto-fixable flag so the shell can colour the
            // quick-fix lightbulb (orange = auto-fix, blue = advisory). Unknown rules → false.
            AutoFixable = meta.AutoFixable,
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
