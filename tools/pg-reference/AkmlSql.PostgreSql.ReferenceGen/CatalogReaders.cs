using System.Text;
using System.Text.RegularExpressions;

namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>One <c>PG_KEYWORD</c> line of <c>src/include/parser/kwlist.h</c>.</summary>
/// <param name="Category">One of <c>reserved</c>, <c>unreserved</c>, <c>column-name</c>, <c>type-function-name</c>.</param>
public sealed record KeywordRecord(string Name, string Category, bool BareLabel);

/// <summary>Reads <c>src/include/parser/kwlist.h</c>: <c>PG_KEYWORD("name", TOKEN, CATEGORY, BARE_LABEL)</c>.</summary>
public static partial class KwlistReader
{
    [GeneratedRegex(@"PG_KEYWORD\(\s*""([^""]+)""\s*,\s*\w+\s*,\s*(\w+)\s*,\s*(\w+)\s*\)")]
    private static partial Regex Line();

    public static IReadOnlyList<KeywordRecord> Read(string text)
    {
        var result = new List<KeywordRecord>();
        foreach (Match m in Line().Matches(text))
            result.Add(new KeywordRecord(m.Groups[1].Value, Map(m.Groups[2].Value), m.Groups[3].Value == "BARE_LABEL"));
        return result;
    }

    public static string Map(string category) => category switch
    {
        "RESERVED_KEYWORD" => "reserved",
        "UNRESERVED_KEYWORD" => "unreserved",
        "COL_NAME_KEYWORD" => "column-name",
        "TYPE_FUNC_NAME_KEYWORD" => "type-function-name",
        _ => "unreserved",
    };
}

/// <summary>
/// Reads the catalog data files (<c>pg_proc.dat</c>, <c>pg_type.dat</c>, <c>pg_operator.dat</c>): a list of
/// <c>{ key =&gt; 'value', ... }</c> records, with <c>#</c> comments. Values are single-quoted (with <c>\'</c> and <c>\\</c>
/// escapes) or bare words; the quotes can hold braces, so the scan is character by character.
/// </summary>
public static class DatFileReader
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Read(string text)
    {
        var records = new List<IReadOnlyDictionary<string, string>>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '#') { i = SkipLine(text, i); continue; }
            if (c != '{') { i++; continue; }

            i++;
            var record = new Dictionary<string, string>(StringComparer.Ordinal);
            while (i < text.Length)
            {
                i = SkipSpaceAndComments(text, i);
                if (i >= text.Length) break;
                if (text[i] == '}') { i++; break; }
                if (text[i] == ',') { i++; continue; }

                var keyStart = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var key = text.Substring(keyStart, i - keyStart);
                i = SkipSpaceAndComments(text, i);
                if (i + 1 < text.Length && text[i] == '=' && text[i + 1] == '>') i += 2;
                i = SkipSpaceAndComments(text, i);

                var value = ReadValue(text, ref i);
                if (key.Length > 0) record[key] = value;
            }
            records.Add(record);
        }
        return records;
    }

    private static string ReadValue(string text, ref int i)
    {
        if (i >= text.Length) return string.Empty;
        if (text[i] == '\'')
        {
            var builder = new StringBuilder();
            i++;
            while (i < text.Length && text[i] != '\'')
            {
                if (text[i] == '\\' && i + 1 < text.Length) { builder.Append(text[i + 1]); i += 2; continue; }
                builder.Append(text[i]);
                i++;
            }
            i++;   // the closing quote
            return builder.ToString();
        }

        var start = i;
        var depth = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c is '[' or '(') depth++;
            else if (c is ']' or ')') depth--;
            else if (depth <= 0 && (c == ',' || c == '}')) break;
            i++;
        }
        return text.Substring(start, i - start).Trim();
    }

    private static int SkipLine(string text, int i)
    {
        while (i < text.Length && text[i] != '\n') i++;
        return i;
    }

    private static int SkipSpaceAndComments(string text, int i)
    {
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) i++;
            else if (text[i] == '#') i = SkipLine(text, i);
            else break;
        }
        return i;
    }
}

/// <summary>One line of <c>src/backend/utils/errcodes.txt</c>.</summary>
/// <param name="Severity"><c>E</c> error, <c>W</c> warning or <c>S</c> success.</param>
/// <param name="Condition">The PL/pgSQL condition name (<c>undefined_table</c>); derived from the macro when the file gives none.</param>
public sealed record SqlStateRecord(string Code, string Severity, string Macro, string Condition, string ClassCode, string ClassName);

/// <summary>Reads <c>errcodes.txt</c>: <c>Section: Class 42 - Syntax Error or Access Rule Violation</c>, then <c>sqlstate E/W/S macro condition</c> lines.</summary>
public static partial class ErrcodesReader
{
    [GeneratedRegex(@"^Section:\s*Class\s+(\w\w)\s*-\s*(.+?)\s*$")]
    private static partial Regex Section();

    [GeneratedRegex(@"^(\w{5})\s+([EWS])\s+(ERRCODE_\w+)(?:\s+(\w+))?\s*$")]
    private static partial Regex Code();

    public static IReadOnlyList<SqlStateRecord> Read(string text)
    {
        var result = new List<SqlStateRecord>();
        string classCode = string.Empty, className = string.Empty;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var section = Section().Match(line);
            if (section.Success)
            {
                classCode = section.Groups[1].Value;
                className = section.Groups[2].Value;
                continue;
            }

            var code = Code().Match(line);
            if (!code.Success) continue;
            var macro = code.Groups[3].Value;
            var condition = code.Groups[4].Success ? code.Groups[4].Value : macro.Substring("ERRCODE_".Length).ToLowerInvariant();
            result.Add(new SqlStateRecord(code.Groups[1].Value, code.Groups[2].Value, macro, condition, classCode, className));
        }
        return result;
    }
}
