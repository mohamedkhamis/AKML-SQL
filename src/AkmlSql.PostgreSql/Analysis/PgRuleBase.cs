using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.PostgreSql.Completion;
using Google.Protobuf;

namespace AkmlSql.PostgreSql.Analysis;

/// <summary>
/// Spec 042 (task T162) - what every PostgreSQL rule shares: its identity and display text, the manual link built from a section
/// name, and a <see cref="PgReporter"/> that turns a position into a finished <see cref="AnalysisDiagnostic"/> (line, column, the
/// severity in effect after the user's overrides). A rule overrides <see cref="Run"/> and reports.
/// </summary>
public abstract class PgRuleBase : IPgAnalysisRule
{
    private const string ManualRoot = "https://www.postgresql.org/docs/18/";

    /// <summary>Where a rule that rests on a product convention, not on the manual, points.</summary>
    public const string ProductDocs = "https://akml.khamis.work/docs/analysis-rules";

    protected PgRuleBase(string ruleId, string category, string name, DiagnosticSeverity severity, string description,
        string manualSection, bool requiresSchema = false, bool autoFixable = false)
    {
        RuleId = ruleId;
        Category = category;
        Name = name;
        DefaultSeverity = severity;
        Description = description;
        RequiresSchema = requiresSchema;
        AutoFixable = autoFixable;
        ReferenceUrl = manualSection.StartsWith("http", StringComparison.Ordinal) ? manualSection : ManualRoot + manualSection + ".html";
        CategoryCode = ruleId.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
    }

    public string RuleId { get; }
    public string CategoryCode { get; }
    public string Category { get; }
    public string Name { get; }
    public string Description { get; }
    public string ReferenceUrl { get; }
    public bool AutoFixable { get; }
    public DiagnosticSeverity DefaultSeverity { get; }
    public bool RequiresSchema { get; }

    public IEnumerable<AnalysisDiagnostic> Analyze(PgAnalysisContext ctx)
    {
        var found = new List<AnalysisDiagnostic>();
        Run(ctx, new PgReporter(this, ctx, found));
        return found;
    }

    /// <summary>Looks at the script and reports what the rule is about.</summary>
    protected abstract void Run(PgAnalysisContext ctx, PgReporter report);
}

/// <summary>
/// Builds the diagnostics of one rule. Every method takes document offsets (UTF-16, end exclusive) or something that maps to
/// them: a token, a parse-tree node (whose position PostgreSQL gives as the start of its first token), a statement.
/// </summary>
public sealed class PgReporter
{
    private readonly IPgAnalysisRule _rule;
    private readonly PgAnalysisContext _ctx;
    private readonly List<AnalysisDiagnostic> _into;
    private readonly DiagnosticSeverity _severity;

    internal PgReporter(IPgAnalysisRule rule, PgAnalysisContext ctx, List<AnalysisDiagnostic> into)
    {
        _rule = rule;
        _ctx = ctx;
        _into = into;
        _severity = ctx.Settings.GetSeverity(rule.RuleId, rule.DefaultSeverity);
    }

    /// <summary>A finding on the range [start, end) of the document.</summary>
    public void At(int start, int end, string message, params AnalysisFixAction[] fixes)
    {
        start = Math.Max(0, Math.Min(start, _ctx.Text.Length));
        end = Math.Max(start, Math.Min(end, _ctx.Text.Length));
        var (line, column) = _ctx.LineColumn(start);
        _into.Add(new AnalysisDiagnostic
        {
            RuleId = _rule.RuleId,
            CategoryCode = _rule.CategoryCode,
            Severity = _severity,
            Message = message,
            StartOffset = start,
            EndOffset = end,
            Line = line,
            Column = column,
            FixActions = fixes,
        });
    }

    /// <summary>A finding on the first line of a statement (the whole statement when it is one line).</summary>
    public void Statement(PgScriptStatement statement, string message, params AnalysisFixAction[] fixes)
    {
        var text = _ctx.Text;
        var end = statement.Start;
        while (end < statement.End && text[end] != '\n' && text[end] != '\r') end++;
        At(statement.Start, end, message, fixes);
    }

    /// <summary>A finding on a significant token.</summary>
    internal void Token(PgSig token, string message, params AnalysisFixAction[] fixes) =>
        At(token.Token.Start, token.Token.End, message, fixes);

    /// <summary>A finding on a parse-tree node: from the start of its first token to the end of that token (a dotted name is kept whole).</summary>
    public void Node(PgScriptStatement statement, IMessage node, string message, params AnalysisFixAction[] fixes)
    {
        var (start, end) = statement.SpanOf(node);
        At(start, end, message, fixes);
    }

    /// <summary>A quick fix that replaces [start, end) with <paramref name="text"/>.</summary>
    public static AnalysisFixAction Replace(string label, int start, int end, string text) => new()
    {
        Label = label,
        FixType = start == end ? FixType.Insert : FixType.Transform,
        ReplacementStart = start,
        ReplacementEnd = end,
        ReplacementText = text,
    };
}
