using System.Diagnostics;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Actions;

/// <summary>
/// Stub: Qualifies object names with schema prefix (e.g., Orders → dbo.Orders).
/// Full implementation deferred — requires DatabaseCache to resolve schemas.
/// </summary>
public class QualifyObjectNamesAction : IFormatAction
{
    public FormatResult Execute(string sql, FormattingProfile profile)
    {
        var sw = Stopwatch.StartNew();
        sw.Stop();

        return new FormatResult
        {
            Success = true,
            FormattedText = sql,
            WasModified = false,
            ValidationPassed = true,
            ElapsedMs = sw.ElapsedMilliseconds,
            Diagnostics = [new FormatDiagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Message = "QualifyObjectNames requires a database connection and schema cache. Not yet implemented."
            }]
        };
    }
}
