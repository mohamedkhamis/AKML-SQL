using System.Diagnostics;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

public class FormatterPipeline
{
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

            // Stage 4: Casing
            var casingEngine = new CasingEngine();
            casingEngine.ApplyCasing(layoutNodes, profile);

            // Stage 5: Emit
            var emitter = new TextEmitter();
            var formatted = emitter.Emit(layoutNodes, profile);

            // Stage 5b: Restore SQLCMD directives
            formatted = sqlcmdPreprocessor.Restore(formatted);

            // Stage 6: Validate
            bool validationPassed = true;
            if (profile.Metadata.Name != "__skip_validation__")
            {
                var validator = new SemanticValidator();
                validationPassed = validator.Validate(sql, formatted, diagnostics);
                if (!validationPassed)
                {
                    formatted = sql; // Return original on validation failure
                }
            }

            // Stage 7: Idempotency check — format again and verify identical result
            if (validationPassed && formatted != sql)
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
                    diagnostics.Add(new FormatDiagnostic
                    {
                        Severity = DiagnosticSeverity.Warning,
                        Message = "Idempotency check failed: second format pass produced different output"
                    });
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
