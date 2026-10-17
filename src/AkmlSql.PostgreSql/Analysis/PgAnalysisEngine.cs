using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Schema;
using AkmlSql.PostgreSql.Language;
using Serilog;

namespace AkmlSql.PostgreSql.Analysis;

/// <summary>
/// Spec 042 (task T162, FR-150 to FR-155, research R3) - runs the PostgreSQL rules over a PostgreSQL script and answers in the
/// shape the T-SQL analyser answers in, so the gutter, the Problems tab, severities, .casettings overrides and the four disable
/// scopes work unchanged.
///
/// <para>
/// The pipeline: settings (global config, then the nearest .casettings) - "code analysis off" returns at once without parsing
/// (FR-155) - then the script is split and every statement parsed. A script with a syntax error produces one finding at the first
/// error and no rule findings (FR-154), because rules would otherwise reason about half a tree. Otherwise each enabled rule runs;
/// a rule that throws is logged and skipped. Suppression comments come from the scanner's comment tokens through the same builder
/// the T-SQL analyser uses; the inline, .casettings, and session suppressions are applied last, so un-suppressing a rule brings
/// its findings back on the very next pass.
/// </para>
/// </summary>
public sealed class PgAnalysisEngine
{
    /// <summary>The id of the one finding a script that does not parse produces. Not a rule: it cannot be switched off from the rules window.</summary>
    public const string SyntaxRuleId = "PGSYN001";

    private const string SyntaxDescription = "The script does not parse. PostgreSQL reports the first syntax error and runs nothing after it, so no other rule is evaluated until it is fixed.";
    private const string SyntaxReference = "https://www.postgresql.org/docs/18/sql-syntax.html";

    private readonly PgRuleRegistry _registry;
    private readonly CaSettingsLoader _settingsLoader;
    private readonly SessionSuppressionStore? _sessionSuppressions;

    public PgAnalysisEngine(PgRuleRegistry registry, CaSettingsLoader settingsLoader, SessionSuppressionStore? sessionSuppressions = null)
    {
        _registry = registry;
        _settingsLoader = settingsLoader;
        _sessionSuppressions = sessionSuppressions;
    }

    public PgRuleRegistry Registry => _registry;

    public CodeAnalysisResponse Analyze(CodeAnalysisRequest request, DatabaseCache? schemaCache, CodeAnalysisSettings globalSettings, CancellationToken ct)
    {
        var directory = string.IsNullOrEmpty(request.FilePath) ? null : Path.GetDirectoryName(request.FilePath);
        var settings = _settingsLoader.Load(directory, globalSettings);

        if (!settings.Enabled) return Respond(request, Array.Empty<AnalysisDiagnostic>());

        var context = PgAnalysisContext.Create(request.DocumentText, schemaCache, settings, ct);
        var suppressions = SuppressionsOf(context, out var meta);

        var found = new List<AnalysisDiagnostic>();
        var syntaxError = context.FirstSyntaxError;
        if (syntaxError != null)
        {
            found.Add(SyntaxFinding(context, syntaxError));
        }
        else
        {
            foreach (var rule in _registry.GetEnabledRules(settings))
            {
                ct.ThrowIfCancellationRequested();
                if (rule.RequiresSchema && schemaCache == null) continue;
                try
                {
                    found.AddRange(rule.Analyze(context));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "PostgreSQL rule {RuleId} threw an exception", rule.RuleId);
                }
            }
        }

        // The meta findings (an unclosed noqa-begin) are about the directives themselves: a range they open must not hide them.
        var shown = found
            .Where(d => !suppressions.IsSuppressed(d.Line, d.RuleId)
                        && !settings.GloballySuppressedRules.Contains(d.RuleId)
                        && _sessionSuppressions?.IsSuppressed(d.RuleId) != true)
            .Concat(meta)
            .OrderBy(d => d.StartOffset)
            .ThenBy(d => d.RuleId, StringComparer.Ordinal)
            .ToList();

        return Respond(request, shown);
    }

    /// <summary>The rules, in the order the rules window lists them. Exposed for the engine's rule listing.</summary>
    public IReadOnlyList<IPgAnalysisRule> Rules => _registry.AllRules;

    private CodeAnalysisResponse Respond(CodeAnalysisRequest request, IReadOnlyList<AnalysisDiagnostic> diagnostics) => new()
    {
        RequestId = request.RequestId,
        AnalyzedVersion = request.DocumentVersion,
        Issues = diagnostics.Select(d => ToIssueInfo(d)).ToArray(),
    };

    private AnalysisDiagnostic SyntaxFinding(PgAnalysisContext context, PgScriptStatement statement)
    {
        var error = statement.Error!;
        var start = Math.Min(error.Offset, context.Text.Length);
        var end = start;
        foreach (var token in context.LexTokens)
        {
            if (token.Start == start && token.Kind != PgTokenKind.Whitespace) { end = token.End; break; }
        }
        if (end == start) end = Math.Min(start + 1, context.Text.Length);

        return new AnalysisDiagnostic
        {
            RuleId = SyntaxRuleId,
            CategoryCode = "PGSYN",
            Severity = DiagnosticSeverity.Error,
            Message = "Syntax error: " + error.Message,
            StartOffset = start,
            EndOffset = end,
            Line = error.Line,
            Column = error.Column,
        };
    }

    // The comments the scanner found, in document order, with the line each starts on.
    private static SuppressionMap SuppressionsOf(PgAnalysisContext context, out List<AnalysisDiagnostic> meta)
    {
        var comments = new List<(string Text, int Line)>();
        foreach (var token in context.LexTokens)
        {
            if (token.Kind is not (PgTokenKind.LineComment or PgTokenKind.BlockComment)) continue;
            comments.Add((context.Text.Substring(token.Start, token.End - token.Start), context.LineColumn(token.Start).Line));
        }
        return SuppressionParser.ParseComments(comments, out meta);
    }

    private CodeIssueInfo ToIssueInfo(AnalysisDiagnostic d)
    {
        var rule = _registry.Find(d.RuleId);
        return new CodeIssueInfo
        {
            RuleId = d.RuleId,
            Severity = (int)d.Severity,
            Message = d.Message,
            StartOffset = d.StartOffset,
            EndOffset = d.EndOffset,
            Line = d.Line,
            Column = d.Column,
            Description = rule?.Description ?? (d.RuleId == SyntaxRuleId ? SyntaxDescription : string.Empty),
            ReferenceUrl = rule?.ReferenceUrl ?? (d.RuleId == SyntaxRuleId ? SyntaxReference : string.Empty),
            AutoFixable = rule?.AutoFixable ?? false,
            FixActions = d.FixActions.Select(f => new FixActionInfo
            {
                Label = f.Label,
                FixType = (int)f.FixType,
                ReplacementStart = f.ReplacementStart,
                ReplacementEnd = f.ReplacementEnd,
                ReplacementText = f.ReplacementText,
                SuppressRuleId = f.SuppressRuleId,
                SuppressScopeCode = f.SuppressScope.HasValue ? (int?)f.SuppressScope : null,
            }).ToArray(),
        };
    }
}
