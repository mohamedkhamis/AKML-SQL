using System.Text.RegularExpressions;

namespace AkmlSql.Engine.Snippets;

public class BuiltInVariableResolver
{
    // Spec 030 T047 / FR-037 — custom $DATE(fmt)$ / $TIME(fmt)$ / $DATETIME(fmt)$ formats. The format is
    // everything between the parens (a snippet format never contains ')'). The fixed $DATE$/$TIME$/
    // $DATETIME$ tokens below have no parens, so they never collide with this pattern.
    private static readonly Regex CustomDateTimeRegex =
        new(@"\$(DATE|TIME|DATETIME)\(([^)]*)\)\$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Resolve(string text, BuiltInVariableContext context) => Resolve(text, context, DateTime.Now);

    /// <summary>
    /// Resolves built-in variables against a caller-supplied <paramref name="now"/> so date/time tokens
    /// are deterministic in tests. The public overload passes <see cref="DateTime.Now"/>.
    /// </summary>
    internal string Resolve(string text, BuiltInVariableContext context, DateTime now)
    {
        var result = text;

        // Custom-format date/time tokens first; an empty or invalid format falls back to the default.
        result = CustomDateTimeRegex.Replace(result, m =>
        {
            var fallback = m.Groups[1].Value.ToUpperInvariant() switch
            {
                "DATE" => "yyyy-MM-dd",
                "TIME" => "HH:mm:ss",
                _ => "yyyy-MM-dd HH:mm:ss",
            };
            var fmt = m.Groups[2].Value;
            if (string.IsNullOrEmpty(fmt)) return now.ToString(fallback);
            try { return now.ToString(fmt); }
            catch (FormatException) { return now.ToString(fallback); }
        });

        result = result.Replace("$DATE$", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$DATETIME$", now.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$TIME$", now.ToString("HH:mm:ss"), StringComparison.OrdinalIgnoreCase);
        // $USER$ prefers the SQL login name when available (e.g. sa, domain\user via SYSTEM_USER);
        // falls back to the OS user when the session has no explicit login (integrated auth).
        var userName = string.IsNullOrEmpty(context.SqlUserName) ? Environment.UserName : context.SqlUserName;
        result = result.Replace("$USER$", userName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$MACHINE$", Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$DATABASE$", context.DatabaseName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$SERVER$", context.ServerName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$SCHEMA$", string.IsNullOrEmpty(context.SchemaName) ? "dbo" : context.SchemaName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$GUID$", Guid.NewGuid().ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$YEAR$", now.Year.ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$FILENAME$", context.FileName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$CLIPBOARD$", context.ClipboardText, StringComparison.OrdinalIgnoreCase);
        // $PASTE$ is a SQL Prompt-compatible alias for $CLIPBOARD$ (FR-037 parity).
        result = result.Replace("$PASTE$", context.ClipboardText, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$SELECTEDTEXT$", context.SelectedText, StringComparison.OrdinalIgnoreCase);
        // $CURSOR$ is handled separately by PlaceholderParser (removed and tracked as position).
        // $SELECTIONSTART$ / $SELECTIONEND$ are likewise deliberately PRESERVED here — they are
        // selection-range markers (not built-in vars), stripped/tracked by SnippetRequestHandler.
        return result;
    }
}
