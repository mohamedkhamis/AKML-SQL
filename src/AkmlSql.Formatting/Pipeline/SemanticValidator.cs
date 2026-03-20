using AkmlSql.Formatting.Layout;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

public class SemanticValidator
{
    public bool Validate(string original, string formatted, List<FormatDiagnostic> diagnostics)
    {
        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);

            using var origReader = new StringReader(original);
            var origScript = parser.Parse(origReader, out var origErrors) as TSqlScript;

            using var fmtReader = new StringReader(formatted);
            var fmtScript = parser.Parse(fmtReader, out var fmtErrors) as TSqlScript;

            if (origScript == null || fmtScript == null)
                return false;

            // Generate normalized scripts for comparison
            var generator = new Sql170ScriptGenerator();
            generator.GenerateScript(origScript, out var origNormalized);
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
