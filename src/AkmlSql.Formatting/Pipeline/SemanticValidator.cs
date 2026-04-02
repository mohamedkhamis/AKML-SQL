using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Stage 6 of the formatting pipeline. Re-parses both the original and formatted SQL,
/// normalizes each with <c>Sql170ScriptGenerator</c>, and compares the results.
/// A mismatch means formatting changed the semantics — the pipeline then returns the original SQL.
/// <para>
/// Use the overload that accepts a pre-parsed <see cref="TSqlScript"/> to avoid re-parsing the original input.
/// </para>
/// </summary>
public class SemanticValidator
{
    /// <summary>
    /// Validates by re-parsing both original and formatted SQL.
    /// Prefer the overload that accepts a pre-parsed script to avoid the redundant original parse.
    /// </summary>
    public bool Validate(string original, string formatted, List<FormatDiagnostic> diagnostics)
    {
        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var origReader = new StringReader(original);
            var origScript = parser.Parse(origReader, out _) as TSqlScript;
            return origScript != null && Validate(origScript, formatted, diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(new FormatDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Message = $"Semantic validation error: {ex.Message}"
            });
            return false;
        }
    }

    /// <summary>
    /// Validates using a pre-parsed original script (avoids re-parsing the original SQL).
    /// </summary>
    public bool Validate(TSqlScript originalScript, string formatted, List<FormatDiagnostic> diagnostics)
    {
        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var fmtReader = new StringReader(formatted);
            var fmtScript = parser.Parse(fmtReader, out _) as TSqlScript;

            if (fmtScript == null)
                return false;

            var generator = new Sql170ScriptGenerator();
            generator.GenerateScript(originalScript, out var origNormalized);
            generator.GenerateScript(fmtScript, out var fmtNormalized);

            if (origNormalized != fmtNormalized)
            {
                diagnostics.Add(new FormatDiagnostic
                {
                    Severity = DiagnosticSeverity.Warning,
                    Message = "Semantic validation failed: formatted output differs from original"
                });
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new FormatDiagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Message = $"Semantic validation error: {ex.Message}"
            });
            return false;
        }
    }
}
