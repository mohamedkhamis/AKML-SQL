using System.Text;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>An argument at an <c>EXEC proc …</c> call site, as seen in the document.</summary>
public sealed class InlineCallArg
{
    /// <summary>The parameter name (<c>@id</c>) for a named argument; null for a positional one.</summary>
    public string? Name { get; init; }

    /// <summary>The raw value text from the document (quotes / N-prefix preserved).</summary>
    public string ValueText { get; init; } = string.Empty;

    /// <summary>True when the call passes this argument as OUTPUT.</summary>
    public bool IsOutput { get; init; }
}

/// <summary>Outcome of an inline attempt: either the inlined SQL, or a reason it was refused.</summary>
public sealed class InlineProcResult
{
    public bool Ok { get; init; }
    public string? InlinedSql { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public static InlineProcResult Fail(string error) => new() { Ok = false, Error = error };

    public static InlineProcResult Success(string sql, IReadOnlyList<string> warnings) =>
        new() { Ok = true, InlinedSql = sql, Warnings = warnings };
}

/// <summary>
/// Spec 030 T063 / FR-020 — the pure transform behind "Inline stored procedure".
///
/// Given a procedure's fetched definition (CREATE/ALTER PROCEDURE … from sys.sql_modules) and the
/// argument list from an <c>EXEC</c> call site, produces the inlined query: the body with each
/// parameter replaced by its call-site argument (or its declared default when omitted). Substitution
/// is token-aware (reusing the same approach as <see cref="InlineExecOperation"/>) so a <c>@p</c>
/// inside a string literal or comment is never touched and an already-substituted value is never
/// re-scanned.
///
/// <para>Deliberately conservative — <see cref="Inline"/> returns a refusal (Ok = false) rather than
/// risk a wrong rewrite for anything beyond a single-query body:</para>
/// <list type="bullet">
///   <item>OUTPUT parameters, or a call that captures an OUTPUT argument.</item>
///   <item>Bodies that aren't exactly one SELECT/INSERT/UPDATE/DELETE/MERGE (after ignoring leading
///         SET-option statements like <c>SET NOCOUNT ON</c>) — i.e. control flow, locals, multiple
///         statements, EXEC, RETURN.</item>
///   <item>A parameter with neither a call-site argument nor a default.</item>
///   <item>Mixed named/positional args, unknown named args, or more args than parameters.</item>
/// </list>
/// No live DB — the definition is fetched by the operation; this stays deterministic and testable.
/// </summary>
public static class InlineStoredProcRewriter
{
    public static InlineProcResult Inline(string? procDefinition, IReadOnlyList<InlineCallArg> callArgs)
    {
        if (string.IsNullOrWhiteSpace(procDefinition))
            return InlineProcResult.Fail("The stored procedure has no definition text to inline.");

        var script = new TsqlParserService().Parse(procDefinition!, out _);
        var collector = new ProcCollector();
        script?.Accept(collector);

        if (!collector.Found)
            return InlineProcResult.Fail(
                "The definition is not a single CREATE/ALTER PROCEDURE and cannot be inlined.");

        var parameters = collector.Parameters;

        if (parameters.Any(p => p.Modifier == ParameterModifier.Output))
            return InlineProcResult.Fail("Procedures with OUTPUT parameters cannot be inlined.");

        var statements = collector.Body?.Statements;
        if (statements is null || statements.Count == 0)
            return InlineProcResult.Fail("The procedure has an empty body; there is nothing to inline.");

        // Ignore leading SET-option toggles (SET NOCOUNT ON, SET ANSI_NULLS ON, …) — they don't
        // affect the inlined query's result. Anything else must reduce to a single query statement.
        var droppedSetCount = statements.Count(s => s is PredicateSetStatement);
        var meaningful = statements.Where(s => s is not PredicateSetStatement).ToList();

        if (meaningful.Count != 1)
            return InlineProcResult.Fail(
                "Only a single-statement procedure body can be inlined (after ignoring SET-option " +
                "statements). Multi-statement bodies, control flow and local variables are not supported.");

        var stmt = meaningful[0];
        if (stmt is not (SelectStatement or InsertStatement or UpdateStatement or DeleteStatement or MergeStatement))
            return InlineProcResult.Fail(
                "Only a single SELECT / INSERT / UPDATE / DELETE / MERGE body can be inlined " +
                "(no control flow, variable declarations, EXEC or RETURN).");

        var (bindings, bindError) = ResolveBindings(parameters, callArgs, procDefinition!);
        if (bindError != null)
            return InlineProcResult.Fail(bindError);

        var bodyText = procDefinition!.Substring(stmt.StartOffset, stmt.FragmentLength);
        var warnings = new List<string>();
        var inlined = Substitute(bodyText, bindings, warnings);

        if (droppedSetCount > 0)
            warnings.Add(
                "Ignored SET-option statement(s) in the procedure body (e.g. SET NOCOUNT ON); they do " +
                "not change the inlined query's result — review if you depend on SET XACT_ABORT semantics.");

        return InlineProcResult.Success(inlined, warnings);
    }

    /// <summary>
    /// Maps the call-site arguments onto the procedure's parameters and resolves each parameter to a
    /// value (supplied argument, else declared default). Returns an error string for any case that
    /// can't be inlined safely.
    /// </summary>
    private static (Dictionary<string, string> Bindings, string? Error) ResolveBindings(
        IList<ProcedureParameter> parameters, IReadOnlyList<InlineCallArg> callArgs, string procDefinition)
    {
        if (callArgs.Any(a => a.IsOutput))
            return (new(), "The EXEC assigns an OUTPUT argument; inlining would drop that assignment.");

        bool anyNamed = callArgs.Any(a => a.Name != null);
        bool anyPositional = callArgs.Any(a => a.Name == null);
        if (anyNamed && anyPositional)
            return (new(), "The EXEC mixes named and positional arguments; only an all-named or " +
                           "all-positional call can be inlined.");

        var paramNames = parameters
            .Select(p => p.VariableName?.Value)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();

        var supplied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (anyNamed)
        {
            var known = new HashSet<string>(paramNames, StringComparer.OrdinalIgnoreCase);
            var unknown = callArgs.Where(a => a.Name != null && !known.Contains(a.Name!))
                                  .Select(a => a.Name!).ToList();
            if (unknown.Count > 0)
                return (new(), $"The EXEC passes argument(s) the procedure does not declare: {string.Join(", ", unknown)}.");

            foreach (var a in callArgs)
                supplied[a.Name!] = a.ValueText;
        }
        else
        {
            if (callArgs.Count > parameters.Count)
                return (new(), "The EXEC supplies more arguments than the procedure declares.");
            for (int i = 0; i < callArgs.Count; i++)
            {
                var pname = parameters[i].VariableName?.Value;
                if (string.IsNullOrEmpty(pname))
                    return (new(), "A procedure parameter has no name; positional arguments cannot be mapped.");
                supplied[pname!] = callArgs[i].ValueText;
            }
        }

        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in parameters)
        {
            var pname = p.VariableName?.Value;
            if (string.IsNullOrEmpty(pname)) continue;

            if (supplied.TryGetValue(pname!, out var v))
                bindings[pname!] = v;
            else if (p.Value != null)
                bindings[pname!] = procDefinition.Substring(p.Value.StartOffset, p.Value.FragmentLength);
            else
                return (new(), $"Parameter {pname} has no argument at the call site and no default value; cannot inline.");
        }

        return (bindings, null);
    }

    /// <summary>
    /// Token-aware single-pass substitution over the body text: replaces only Variable tokens that
    /// have a binding, emitting everything else (keywords, string literals, comments) verbatim.
    /// Warns when a parameter is referenced more than once (its argument is substituted at each use).
    /// </summary>
    private static string Substitute(string bodyText, Dictionary<string, string> bindings, List<string> warnings)
    {
        var sb = new StringBuilder();
        var useCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var tok in new TsqlParserService().GetTokenStream(bodyText))
        {
            if (tok.TokenType == TSqlTokenType.Variable && bindings.TryGetValue(tok.Text, out var value))
            {
                useCount[tok.Text] = useCount.TryGetValue(tok.Text, out var c) ? c + 1 : 1;
                HeavyweightOperationBase.AppendSubstitutedValue(sb, value); // guard '--' / '/*' fusion
                continue;
            }
            sb.Append(tok.Text);
        }

        var multiUse = useCount.Where(kv => kv.Value > 1).Select(kv => kv.Key).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (multiUse.Count > 0)
            warnings.Add(
                $"Parameter(s) {string.Join(", ", multiUse)} are used more than once; each occurrence is " +
                "replaced by the argument expression — review for repeated evaluation or side effects.");

        return sb.ToString();
    }

    private sealed class ProcCollector : TSqlFragmentVisitor
    {
        public bool Found { get; private set; }
        public IList<ProcedureParameter> Parameters { get; private set; } = new List<ProcedureParameter>();
        public StatementList? Body { get; private set; }

        public override void Visit(CreateProcedureStatement node) => Capture(node.Parameters, node.StatementList);
        public override void Visit(AlterProcedureStatement node) => Capture(node.Parameters, node.StatementList);

        private void Capture(IList<ProcedureParameter> parameters, StatementList? body)
        {
            if (Found) return; // first procedure only
            Found = true;
            Parameters = parameters;
            Body = body;
        }
    }
}
