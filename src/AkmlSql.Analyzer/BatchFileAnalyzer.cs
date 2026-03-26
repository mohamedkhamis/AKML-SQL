using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Analysis;
// ReSharper disable NullableWarningSuppressionIsUsed
#pragma warning disable IL2026

namespace AkmlSql.Analyzer;

/// <summary>
/// Discovers and analyzes SQL files in a directory (or single file) without an out-of-process engine.
/// Uses <see cref="AnalysisEngine"/> directly via in-process analysis.
/// </summary>
internal sealed class BatchFileAnalyzer
{
    private readonly RuleRegistry     _registry;
    private readonly CaSettingsLoader _loader;
    private readonly TsqlParserService _parser;

    public BatchFileAnalyzer(string? settingsPath = null)
    {
        _parser   = new TsqlParserService();
        _parser.SetServerVersion(160);
        _registry = new RuleRegistry();
        _loader   = new CaSettingsLoader();

        if (!string.IsNullOrEmpty(settingsPath))
            _explicitSettingsPath = settingsPath;
    }

    private readonly string? _explicitSettingsPath;

    /// <summary>
    /// Analyzes all .sql files in <paramref name="directory"/> and returns per-file results.
    /// </summary>
    public IEnumerable<(string FilePath, IReadOnlyList<AnalysisDiagnostic> Diagnostics)>
        AnalyzeDirectory(string directory, bool recursive, AnalyzerOptions opts, CancellationToken ct)
    {
        var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(directory, "*.sql", searchOpt))
        {
            ct.ThrowIfCancellationRequested();
            var diags = AnalyzeFile(file, opts, ct);
            yield return (file, diags);
        }
    }

    /// <summary>
    /// Analyzes a single SQL file and returns diagnostics.
    /// </summary>
    public IReadOnlyList<AnalysisDiagnostic> AnalyzeFile(string filePath, AnalyzerOptions opts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var sql = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(sql)) return [];

        var fileDir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var globalSettings = new CodeAnalysisSettings { Enabled = true };
        var resolved = string.IsNullOrEmpty(_explicitSettingsPath)
            ? _loader.Load(fileDir, globalSettings)
            : LoadFromExplicitPath();

        var rules  = FilterRules(_registry.GetEnabledRules(resolved), opts);
        var script = _parser.Parse(sql, out _);
        if (script == null) return [];

        var tokens      = _parser.GetTokenStream(sql);
        var suppressions = SuppressionParser.Parse(tokens, out var metaDiags);

        var results = new List<AnalysisDiagnostic>(metaDiags);

        foreach (var batch in script.Batches)
        {
            ct.ThrowIfCancellationRequested();
            var ctx = new AnalysisContext
            {
                Script       = script,
                CurrentBatch = batch,
                Tokens       = tokens,
                DocumentText = sql,
                SessionId    = "cli",
                SchemaCache  = null,
                Settings     = resolved,
                Suppressions = suppressions
            };

            foreach (var rule in rules)
            {
                if (rule.RequiresSchema) continue;
                results.AddRange(rule.Analyze(ctx));
            }
        }

        return results
            .FindAll(d => !suppressions.IsSuppressed(d.Line, d.RuleId)
                       && d.Severity >= opts.MinSeverity)
            .AsReadOnly();
    }

    private static List<IAnalysisRule> FilterRules(IReadOnlyList<IAnalysisRule> all, AnalyzerOptions opts)
    {
        var result = new List<IAnalysisRule>();
        foreach (var rule in all)
        {
            if (opts.OnlyRules    != null && !opts.OnlyRules.Contains(rule.RuleId))    continue;
            if (opts.ExcludeRules != null &&  opts.ExcludeRules.Contains(rule.RuleId)) continue;
            result.Add(rule);
        }
        return result;
    }

    private ResolvedAnalysisSettings LoadFromExplicitPath()
    {
        try
        {
            var json = File.ReadAllText(_explicitSettingsPath!);
            var ca   = System.Text.Json.JsonSerializer.Deserialize<CaSettings>(json,
                           new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var settings = new ResolvedAnalysisSettings { Enabled = true };
            if (ca?.Rules != null)
            {
                foreach (var (id, cfg) in ca.Rules)
                {
                    settings.EffectiveRules[id] = new ResolvedRuleConfig
                    {
                        Enabled  = cfg.Enabled,
                        Severity = cfg.Severity.ToLowerInvariant() switch
                        {
                            "error"       => DiagnosticSeverity.Error,
                            "warning"     => DiagnosticSeverity.Warning,
                            "information" => DiagnosticSeverity.Information,
                            "hint"        => DiagnosticSeverity.Hint,
                            _             => DiagnosticSeverity.Warning
                        }
                    };
                }
            }
            return settings;
        }
        catch
        {
            return new ResolvedAnalysisSettings { Enabled = true };
        }
    }
}
