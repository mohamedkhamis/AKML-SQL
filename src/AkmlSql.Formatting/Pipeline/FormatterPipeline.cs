using System.Diagnostics;
using AkmlSql.Formatting.Actions;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Rules;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Orchestrates the 7-stage SQL formatting pipeline:
/// NoformatScanner → SqlcmdPreprocessor → TSql170Parser → AstAnnotator →
/// LayoutEngine → CasingEngine → TextEmitter → SemanticValidator → IdempotencyCheck.
/// <para>
/// Stage 6 (semantic validation) failure causes the original SQL to be returned unchanged.
/// Stage 7 (idempotency) can be suppressed via <c>ProfileMetadata.EnableIdempotencyCheck = false</c>.
/// </para>
/// </summary>
public class FormatterPipeline
{
    /// <summary>
    /// Spec 030 R1 — layout-rule passes applied after <c>LayoutEngine.BuildLayout</c> and before
    /// casing. Defaults to <see cref="RuleEngine.DefaultOrder"/> (all six rule sets, T008 production
    /// enable): the per-group golden-oracle rework + the ORDER/GROUP list-boundary fix cleared the
    /// idempotency + semantic-validation + visual-indent gates. Set explicitly to a subset to scope
    /// the passes, or to <c>null</c> to disable them entirely (used by the R1 inspection/spike tests
    /// to capture a rules-off baseline). See specs/030-sqlprompt-parity-closure/research.md (R1).
    /// </summary>
    public IReadOnlyList<IRuleSet>? LayoutRules { get; set; } = RuleEngine.DefaultOrder;

    private void ApplyLayoutRules(List<LayoutNode> nodes, FormattingProfile profile)
    {
        if (LayoutRules is null) return;
        foreach (var ruleSet in LayoutRules)
            ruleSet.Apply(nodes, profile);

        // Finalization: keep a unary sign hugging its operand ("-1", not "- 1"). This runs after
        // every rule because the collapse passes (one per rule set: Dml/Ddl/List/Parenthesis/
        // ControlFlow) re-join an exploded list and force one space before each non-comma token —
        // which would re-separate a sign from its operand. A single post-collapse pass is the one
        // chokepoint that catches every collapse path. See spec 030 T009 (#1).
        NormalizeUnarySignSpacing(nodes);
        NormalizeSemicolonSpacing(nodes);
        NormalizeMergeWhenLayout(nodes, profile.Dml.MergeWhenOnNewLine);

        // Alias alignment is line GEOMETRY, so it must see the final line shapes — after every
        // rule set's collapse passes (ParenthesisRules re-joins exploded function-call parens
        // after ListRules ran) and after the spacing normalizers above, which change widths.
        ListRules.AlignAliases(nodes, profile.List);

        // Max-line wrapping runs LAST — it is the hard width constraint over whatever geometry
        // the rules, normalizers, and alignment produced (FR-002, spec 030 T012).
        LineWrapper.Wrap(nodes, profile);

        // Right-alignment (operators/IN items) writes absolute line-start columns the tab grid
        // can't hit; it must see the final line shapes, so it runs after wrapping (spec 030 T013).
        // No-op unless a style opts into operators/inStatements alignment "rightAligned".
        RightAligner.Align(nodes, profile);
    }

    /// <summary>
    /// A statement terminator hugs the token before it ("SELECT 1;", "DELETE;" — never "1 ;").
    /// The base layout already emits semicolons with zero preceding spaces; only the collapse
    /// passes re-space them (one space before every non-comma token), so this is the same
    /// post-collapse chokepoint as <see cref="NormalizeUnarySignSpacing"/>.
    /// </summary>
    private static void NormalizeSemicolonSpacing(List<LayoutNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.TokenType == TSqlTokenType.Semicolon && !node.IsInNoformatRegion
                && node.PrecedingBreak == BreakType.None)
                node.PrecedingSpaces = 0;
        }
    }

    /// <summary>
    /// Each MERGE <c>WHEN</c> clause must start its own line at the MERGE statement's indent.
    /// <c>DmlRules.ApplyMergeWhenOnNewLine</c> breaks them early, but the rule sets' later collapse
    /// passes re-cram a WHEN onto the preceding SET/VALUES/INSERT clause — this post-collapse pass
    /// re-asserts the break (the last word on MERGE WHEN geometry). Scoped strictly to WHEN tokens
    /// inside a MERGE, so it cannot affect any non-MERGE statement (spec 030 T009 — MERGE layout).
    /// </summary>
    private static void NormalizeMergeWhenLayout(List<LayoutNode> nodes, bool enabled)
    {
        if (!enabled) return;   // honour dml.mergeWhenOnNewLine (same gate as DmlRules)
        var scope = new MergeScopeTracker();
        foreach (var node in nodes)
        {
            if (node.IsInNoformatRegion) continue;
            if (scope.Advance(node)) continue;
            // Force EVERY top-level MERGE match clause onto its own line at the MERGE indent —
            // unconditionally (not only when currently unbroken), because a collapse may have left
            // it broken at the wrong indent (crammed under the preceding SET/VALUES clause).
            if (scope.InMerge && scope.CaseDepth == 0 && node.TokenType == TSqlTokenType.When)
            {
                node.PrecedingBreak = BreakType.NewLine;
                node.PrecedingSpaces = 0;
                node.IndentLevel = scope.MergeIndent;
            }
        }
    }

    /// <summary>
    /// Sets the operand directly after a unary <c>-</c>/<c>+</c> sign to zero preceding spaces, so a
    /// sign hugs its operand on the same line. A sign is unary (vs. binary subtraction) when the
    /// token two back does not end a value — see <see cref="TokenClassification.IsUnarySign"/>. Only
    /// touches inline tokens (the sign and operand already on one line); a sign/operand split across
    /// a line break is left to the layout rules. Noformat regions are never altered.
    /// </summary>
    private static void NormalizeUnarySignSpacing(List<LayoutNode> nodes)
    {
        for (int i = 1; i < nodes.Count; i++)
        {
            var operand = nodes[i];
            if (operand.IsInNoformatRegion || operand.PrecedingBreak != BreakType.None || operand.PrecedingSpaces == 0)
                continue;
            var beforeSign = i >= 2 ? nodes[i - 2].TokenType : (TSqlTokenType?)null;
            if (TokenClassification.IsUnarySign(nodes[i - 1].TokenType, beforeSign))
                operand.PrecedingSpaces = 0;
        }
    }

    /// <summary>
    /// Performs a raw format pass without validation or idempotency checking.
    /// Returns null on parse failure or error, with the exception captured in the out parameter.
    /// </summary>
    private string? FormatInternal(string sql, FormattingProfile profile, out Exception? error)
    {
        error = null;
        try
        {
            var noformatScanner = new NoformatScanner();
            var noformatRegions = noformatScanner.Scan(sql);
            var sqlcmdPreprocessor = new SqlcmdPreprocessor();
            var preprocessedSql = sqlcmdPreprocessor.Preprocess(sql, noformatRegions);

            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(preprocessedSql);
            var script = parser.Parse(reader, out _) as TSqlScript;
            var tokens = script?.ScriptTokenStream ?? (IList<TSqlParserToken>)[];

            if (script == null || script.Batches.Count == 0)
                return null;

            var annotator = new AstAnnotator();
            var comments = annotator.AttachComments(tokens);

            var layoutEngine = new LayoutEngine();
            var layoutNodes = layoutEngine.BuildLayout(script, tokens, comments, profile, noformatRegions);

            ApplyLayoutRules(layoutNodes, profile);

            var casingEngine = new CasingEngine();
            casingEngine.ApplyCasing(layoutNodes, profile);

            var emitter = new TextEmitter();
            var formatted = emitter.Emit(layoutNodes, profile);
            return sqlcmdPreprocessor.Restore(formatted);
        }
        catch (Exception ex)
        {
            error = ex;
            return null;
        }
    }

    /// <summary>
    /// Formats <paramref name="sql"/> using the specified <paramref name="profile"/>.
    /// Returns a <see cref="FormatResult"/> containing the formatted text, elapsed time, and any diagnostics.
    /// If semantic validation fails, <see cref="FormatResult.FormattedSql"/> equals the original input.
    /// </summary>
    public FormatResult Format(string sql, FormattingProfile profile)
    {
        var sw = Stopwatch.StartNew();
        var diagnostics = new List<FormatDiagnostic>();

        try
        {
            // Stage 0a: Scan for noformat regions
            var noformatScanner = new NoformatScanner();
            var noformatRegions = noformatScanner.Scan(sql);

            // Stage 0b: Preprocess SQLCMD directives
            var sqlcmdPreprocessor = new SqlcmdPreprocessor();
            var preprocessedSql = sqlcmdPreprocessor.Preprocess(sql, noformatRegions);

            // Stage 1: Parse
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(preprocessedSql);
            var script = parser.Parse(reader, out var errors) as TSqlScript;
            var tokens = script?.ScriptTokenStream ?? (IList<TSqlParserToken>)[];

            if (script == null || script.Batches.Count == 0)
            {
                return new FormatResult
                {
                    Success = false,
                    FormattedText = sql,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Diagnostics = [new FormatDiagnostic { Severity = DiagnosticSeverity.Error, Message = "Failed to parse SQL" }]
                };
            }

            if (errors.Count > 0)
            {
                foreach (var e in errors)
                    diagnostics.Add(new FormatDiagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Message = e.Message,
                        Line = e.Line,
                        Offset = e.Offset
                    });
            }

            // Stage 2: Annotate
            var annotator = new AstAnnotator();
            var comments = annotator.AttachComments(tokens);

            // Stage 3: Layout (pass noformat regions to mark tokens)
            var layoutEngine = new LayoutEngine();
            var layoutNodes = layoutEngine.BuildLayout(script, tokens, comments, profile, noformatRegions);

            // Stage 3b (Spec 030 R1 spike): optional layout-rule passes, off by default
            ApplyLayoutRules(layoutNodes, profile);

            // Stage 4: Casing
            var casingEngine = new CasingEngine();
            casingEngine.ApplyCasing(layoutNodes, profile);

            // Stage 5: Emit
            var emitter = new TextEmitter();
            var formatted = emitter.Emit(layoutNodes, profile);

            // Stage 5b: Restore SQLCMD directives
            formatted = sqlcmdPreprocessor.Restore(formatted);

            // Stage 6: Validate (pass the already-parsed script to avoid re-parsing the original)
            bool validationPassed = true;
            if (!profile.Metadata.SkipValidation)
            {
                var validator = new SemanticValidator();
                validationPassed = validator.Validate(script, formatted, diagnostics);
                if (!validationPassed)
                {
                    formatted = sql; // Return original on validation failure
                }
            }

            // Stage 7: Idempotency check — format again and verify identical result.
            // Spec 032 J2 (FR-030): the check used to be DETECT-ONLY — it found the
            // divergence, appended a Warning, and still shipped the divergent first pass
            // (which the web editor then silently dropped the warning for). Now the
            // CONVERGED second pass is returned when it differs, is non-empty, and
            // passes its own semantic re-validation; the Warning stays surfaced.
            if (validationPassed && formatted != sql && profile.Metadata.EnableIdempotencyCheck)
            {
                var secondPass = FormatInternal(formatted, profile, out var idempotencyError);
                if (idempotencyError != null)
                {
                    diagnostics.Add(new FormatDiagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Message = $"Idempotency check error: {idempotencyError.Message}"
                    });
                }
                else if (secondPass != null && secondPass != formatted)
                {
                    bool secondPassValid = profile.Metadata.SkipValidation ||
                        new SemanticValidator().Validate(script, secondPass, diagnostics);
                    if (!string.IsNullOrWhiteSpace(secondPass) && secondPassValid)
                    {
                        formatted = secondPass;
                        diagnostics.Add(new FormatDiagnostic
                        {
                            Severity = DiagnosticSeverity.Warning,
                            Message = "Formatting converged on a second pass (a layout rule is not idempotent); the converged result was returned"
                        });
                    }
                    else
                    {
                        diagnostics.Add(new FormatDiagnostic
                        {
                            Severity = DiagnosticSeverity.Warning,
                            Message = "Idempotency check failed: second format pass produced different output"
                        });
                    }
                }
            }

            // Stage 8 (Spec 030 FR-004 R2): format-time actions — apply enabled FormatActionConfig
            // flags after the main pipeline pass. Only runs when validation passed (preserving the
            // contract that a validation-failed return keeps the original SQL untouched).
            // Skipped: CasingOnly (pipeline already cased), AddAsKeyword (add-path is an AST stub),
            // ExpandWildcards + QualifyObjectNames (schema stubs). ApplyLayout / ApplyCasing gate
            // the main pipeline stages, not the action chain.
            // NOTE: false for AddSquareBrackets means "off", NOT "remove brackets" — there is no
            // RemoveSquareBrackets flag in FormatActionConfig, so "false" must be a no-op.
            if (validationPassed)
            {
                var actions = profile.FormatActions;

                // InsertSemicolons: add terminators where absent
                if (actions.InsertSemicolons)
                {
                    var r = new InsertSemicolonsAction().Execute(formatted, profile);
                    if (r.Success) formatted = r.FormattedText;
                }
                // RemoveSemicolons: strip all terminators (mutually exclusive with Insert, but
                // if both are set the caller is responsible — we run them in declaration order)
                else if (actions.RemoveSemicolons)
                {
                    var r = new RemoveSemicolonsAction().Execute(formatted, profile);
                    if (r.Success) formatted = r.FormattedText;
                }

                // AddSquareBrackets: bracket all plain identifiers (opt-in only; false = no-op)
                if (actions.AddSquareBrackets)
                {
                    var r = new ToggleBracketsAction().Execute(formatted, profile);
                    if (r.Success) formatted = r.FormattedText;
                }
            }

            sw.Stop();
            return new FormatResult
            {
                Success = true,
                FormattedText = formatted,
                WasModified = formatted != sql,
                ValidationPassed = validationPassed,
                ElapsedMs = sw.ElapsedMilliseconds,
                Diagnostics = diagnostics.ToArray()
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new FormatResult
            {
                Success = false,
                FormattedText = sql,
                ElapsedMs = sw.ElapsedMilliseconds,
                Diagnostics = [new FormatDiagnostic { Severity = DiagnosticSeverity.Error, Message = ex.Message }]
            };
        }
    }
}
