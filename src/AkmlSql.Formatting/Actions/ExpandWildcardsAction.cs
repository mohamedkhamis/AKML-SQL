using System.Diagnostics;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Actions;

/// <summary>
/// Stub: Expands SELECT * into explicit column lists.
/// Full implementation deferred — requires DatabaseCache to resolve column names.
/// </summary>
public class ExpandWildcardsAction : IFormatAction
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
                Message = "ExpandWildcards requires a database connection and schema cache. Not yet implemented."
            }]
        };
    }
}
