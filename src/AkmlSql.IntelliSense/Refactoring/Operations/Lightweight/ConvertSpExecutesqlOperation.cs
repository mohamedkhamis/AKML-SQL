using System.Text;
using System.Text.RegularExpressions;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Converts <c>EXEC sp_executesql N'...', N'@p1 type, ...', @p1 = value, ...</c> calls
/// into static SQL by substituting parameter values directly into the template string.
/// Uses text-level parsing (not ScriptDom) because sp_executesql arguments are string
/// literals that the parser treats as opaque values.
/// </summary>
public class ConvertSpExecutesqlOperation : ILightweightOperation
{
    // Matches EXEC[UTE] sp_executesql (case-insensitive, allows optional schema prefix)
    private static readonly Regex SpExecutesqlPattern = new(
        @"(?<prefix>EXEC(?:UTE)?)\s+(?:(?:dbo|sys)\.)?sp_executesql\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            var text = context.DocumentText;
            var warnings = new List<string>();

            // Find all sp_executesql calls and process from end to start to preserve offsets
            var matches = SpExecutesqlPattern.Matches(text);
            if (matches.Count == 0)
                return (context.DocumentText, []);

            var replacements = new List<(int start, int length, string replacement)>();

            foreach (Match match in matches)
            {
                // If there's a selection, only process matches within it
                if (context.HasSelection)
                {
                    if (match.Index < context.SelectionStart ||
                        match.Index >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                var argsStart = match.Index + match.Length;
                var result = ParseSpExecutesqlCall(text, argsStart, warnings);
                if (result == null) continue;

                var (endOffset, staticSql) = result.Value;
                replacements.Add((match.Index, endOffset - match.Index, staticSql));
            }

            // Apply replacements from end to start
            replacements.Sort((a, b) => b.start.CompareTo(a.start));

            foreach (var (start, length, replacement) in replacements)
            {
                text = text[..start] + replacement + text[(start + length)..];
            }

            return (text, [.. warnings]);
        }
        catch (Exception)
        {
            return (context.DocumentText, []);
        }
    }

    /// <summary>
    /// Parses the sp_executesql arguments starting from the position right after "sp_executesql ".
    /// Returns the end offset of the call and the resulting static SQL, or null on failure.
    /// </summary>
    private static (int endOffset, string staticSql)? ParseSpExecutesqlCall(
        string text, int start, List<string> warnings)
    {
        // Extract the arguments (up to 3: template, param defs, param values)
        var args = ExtractArguments(text, start);
        if (args == null || args.Count == 0)
        {
            warnings.Add("Could not parse sp_executesql arguments.");
            return null;
        }

        // Arg 1: SQL template — must be a string literal N'...' or '...'
        var templateArg = args[0];
        var template = ExtractStringLiteral(templateArg.text);
        if (template == null)
        {
            warnings.Add("sp_executesql first argument is not a string literal.");
            return null;
        }

        // If only one argument, no parameters — just return the template
        if (args.Count == 1)
            return (args[^1].end, template);

        // Arg 2: Parameter definitions — N'@p1 int, @p2 nvarchar(50)'
        Dictionary<string, string>? paramTypes = null;
        if (args.Count >= 2)
        {
            var paramDefStr = ExtractStringLiteral(args[1].text);
            if (paramDefStr != null)
                paramTypes = ParseParameterDefinitions(paramDefStr);
        }

        // Arg 3+: Parameter values — @p1 = value, @p2 = N'Smith'
        // These may be a single argument or multiple comma-separated arguments
        Dictionary<string, string>? paramValues = null;
        if (args.Count >= 3)
        {
            // Collect all remaining argument texts into one string
            var valueArgs = new List<string>();
            for (int i = 2; i < args.Count; i++)
                valueArgs.Add(args[i].text);
            var combinedValues = string.Join(", ", valueArgs);
            paramValues = ParseParameterValues(combinedValues);
        }

        if (paramValues == null || paramValues.Count == 0)
            return (args[^1].end, template);

        // Substitute parameters into template
        var result = SubstituteParameters(template, paramValues);
        return (args[^1].end, result);
    }

    /// <summary>
    /// Extracts top-level comma-separated arguments from the text, handling nested parentheses
    /// and string literals. Returns (trimmed text, end offset) for each argument.
    /// </summary>
    public static List<(string text, int end)>? ExtractArguments(string text, int start)
    {
        var args = new List<(string text, int end)>();
        var sb = new StringBuilder();
        int parenDepth = 0;
        bool inString = false;
        bool isNString = false;
        int i = start;

        while (i < text.Length)
        {
            char c = text[i];

            // Handle string literals
            if (inString)
            {
                sb.Append(c);
                if (c == '\'')
                {
                    // Check for escaped quote ''
                    if (i + 1 < text.Length && text[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i += 2;
                        continue;
                    }
                    inString = false;
                }
                i++;
                continue;
            }

            // Start of string literal
            if (c == '\'' || (c == 'N' && i + 1 < text.Length && text[i + 1] == '\''))
            {
                inString = true;
                isNString = c == 'N';
                sb.Append(c);
                if (isNString)
                {
                    i++;
                    sb.Append(text[i]); // append the '
                }
                i++;
                continue;
            }

            // Parentheses tracking (for nested expressions)
            if (c == '(') { parenDepth++; sb.Append(c); i++; continue; }
            if (c == ')') { parenDepth--; if (parenDepth < 0) break; sb.Append(c); i++; continue; }

            // Top-level comma = argument separator
            if (c == ',' && parenDepth == 0)
            {
                args.Add((sb.ToString().Trim(), i));
                sb.Clear();
                i++;
                continue;
            }

            // Statement terminators
            if (c == ';')
            {
                // End of statement
                args.Add((sb.ToString().Trim(), i + 1));
                return args;
            }

            // Newlines — check if the next non-whitespace is a new statement keyword
            if (c == '\r' || c == '\n')
            {
                // Peek ahead past whitespace to see if a new statement starts
                int peek = i;
                while (peek < text.Length && (text[peek] == '\r' || text[peek] == '\n' || text[peek] == ' ' || text[peek] == '\t'))
                    peek++;

                if (peek < text.Length && IsStatementStart(text, peek))
                {
                    // This newline ends the current call
                    var pendingArg = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(pendingArg))
                        args.Add((pendingArg, i));
                    return args.Count > 0 ? args : null;
                }

                // Otherwise it's a continuation of the current call
                sb.Append(c);
                i++;
                continue;
            }

            // Line comment
            if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                // Skip to end of line
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }

            // Block comment
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i += 2; // skip */
                continue;
            }

            sb.Append(c);
            i++;
        }

        // Add the last argument
        var lastArg = sb.ToString().Trim();
        if (!string.IsNullOrEmpty(lastArg))
            args.Add((lastArg, i));

        return args.Count > 0 ? args : null;
    }

    /// <summary>
    /// Extracts the content of a T-SQL string literal, handling N'' prefix and escaped quotes.
    /// Returns null if the input is not a valid string literal.
    /// </summary>
    public static string? ExtractStringLiteral(string value)
    {
        var trimmed = value.Trim();

        // Remove N prefix if present
        if (trimmed.StartsWith("N'", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        if (trimmed.Length < 2 || trimmed[0] != '\'' || trimmed[^1] != '\'')
            return null;

        // Remove outer quotes and unescape doubled quotes
        var inner = trimmed[1..^1];
        return inner.Replace("''", "'");
    }

    /// <summary>
    /// Parses parameter definitions like "@id int, @name nvarchar(50)" into a name→type map.
    /// </summary>
    public static Dictionary<string, string> ParseParameterDefinitions(string definitions)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(definitions)) return result;

        // Split on top-level commas (respecting parentheses for types like nvarchar(50))
        var parts = SplitTopLevel(definitions, ',');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Format: @paramName type [(size)] [OUTPUT]
            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0) continue;

            var paramName = trimmed[..spaceIdx].Trim();
            var typePart = trimmed[(spaceIdx + 1)..].Trim();

            // Remove OUTPUT/OUT suffix if present
            typePart = Regex.Replace(typePart, @"\s+(OUTPUT|OUT)\s*$", "", RegexOptions.IgnoreCase).Trim();

            result[paramName] = typePart;
        }

        return result;
    }

    /// <summary>
    /// Parses parameter value assignments like "@id = 5, @name = N'Smith'" into a name→value map.
    /// </summary>
    public static Dictionary<string, string> ParseParameterValues(string valueAssignments)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(valueAssignments)) return result;

        // Split on top-level commas
        var parts = SplitTopLevel(valueAssignments, ',');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Format: @paramName = value
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx < 0) continue;

            var paramName = trimmed[..eqIdx].Trim();
            var valuePart = trimmed[(eqIdx + 1)..].Trim();

            result[paramName] = valuePart;
        }

        return result;
    }

    /// <summary>
    /// Substitutes parameter placeholders in the template with their literal values.
    /// Parameters are replaced longest-name-first to avoid partial matches (e.g., @id vs @id2).
    /// </summary>
    public static string SubstituteParameters(string template, Dictionary<string, string> paramValues)
    {
        // Sort by parameter name length descending to replace longer names first
        var sorted = paramValues.OrderByDescending(kv => kv.Key.Length).ToList();

        // Pre-resolve substitution values (strip N' prefix from Unicode string literals)
        var substitutions = new List<(string paramName, string value)>();
        foreach (var (paramName, rawValue) in sorted)
        {
            var v = rawValue;
            if (v.StartsWith("N'", StringComparison.OrdinalIgnoreCase) && v.EndsWith("'"))
                v = v[1..]; // N'Smith' → 'Smith'
            substitutions.Add((paramName, v));
        }

        // Walk the template tracking string literal context.
        // Only substitute parameters that appear OUTSIDE of string literals.
        var result = new StringBuilder(template.Length);
        int pos = 0;

        while (pos < template.Length)
        {
            // String literal: copy verbatim (handles escaped '' pairs)
            if (template[pos] == '\'')
            {
                result.Append('\'');
                pos++;
                while (pos < template.Length)
                {
                    result.Append(template[pos]);
                    if (template[pos] == '\'')
                    {
                        pos++;
                        if (pos < template.Length && template[pos] == '\'')
                        { result.Append('\''); pos++; } // escaped ''
                        else break; // end of literal
                    }
                    else pos++;
                }
                continue;
            }

            // Parameter match at current position
            bool matched = false;
            if (template[pos] == '@')
            {
                foreach (var (paramName, value) in substitutions)
                {
                    if (pos + paramName.Length <= template.Length &&
                        template.Substring(pos, paramName.Length).Equals(paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        int after = pos + paramName.Length;
                        if (after >= template.Length || !IsParamChar(template[after]))
                        {
                            result.Append(value);
                            pos += paramName.Length;
                            matched = true;
                            break;
                        }
                    }
                }
            }

            if (!matched) { result.Append(template[pos]); pos++; }
        }

        return result.ToString();
    }

    private static bool IsParamChar(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
        (c >= '0' && c <= '9') || c == '_' || c == '@' || c == '#' || c == '$';

    /// <summary>
    /// Checks if the text at the given position starts with a SQL statement keyword
    /// (EXEC, EXECUTE, SELECT, INSERT, UPDATE, DELETE, IF, BEGIN, GO, etc.).
    /// </summary>
    private static bool IsStatementStart(string text, int pos)
    {
        ReadOnlySpan<string> keywords =
        [
            "EXEC ", "EXECUTE ", "SELECT ", "INSERT ", "UPDATE ", "DELETE ",
            "IF ", "BEGIN ", "GO", "DECLARE ", "SET ", "WITH ", "MERGE ",
            "CREATE ", "ALTER ", "DROP ", "PRINT ", "RAISERROR", "THROW "
        ];

        var remaining = text.AsSpan(pos);
        foreach (var kw in keywords)
        {
            if (remaining.Length >= kw.Length &&
                remaining[..kw.Length].ToString().Equals(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Splits a string on a delimiter character, respecting parentheses and string literals.
    /// </summary>
    private static List<string> SplitTopLevel(string input, char delimiter)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        int parenDepth = 0;
        bool inString = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (inString)
            {
                sb.Append(c);
                if (c == '\'')
                {
                    if (i + 1 < input.Length && input[i + 1] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                        continue;
                    }
                    inString = false;
                }
                continue;
            }

            if (c == '\'')
            {
                inString = true;
                sb.Append(c);
                continue;
            }

            if (c == '(') { parenDepth++; sb.Append(c); continue; }
            if (c == ')') { parenDepth--; sb.Append(c); continue; }

            if (c == delimiter && parenDepth == 0)
            {
                parts.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0)
            parts.Add(sb.ToString());

        return parts;
    }
}
