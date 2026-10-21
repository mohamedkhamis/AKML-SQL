using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>
/// Spec 042 (T091) - the little SGML the PostgreSQL documentation is written in is not XML (entities such as <c>&amp;mdash;</c>
/// are declared elsewhere, tags are not always closed), so the readers work on text. This turns a fragment into the plain text
/// a person would read: tags removed, entities decoded, white space collapsed.
/// </summary>
public static partial class SgmlText
{
    // The entities the documentation uses in running text. Anything else (a version number, a product name) is dropped.
    private static readonly Dictionary<string, string> Entities = new(StringComparer.Ordinal)
    {
        ["lt"] = "<", ["gt"] = ">", ["amp"] = "&", ["quot"] = "\"", ["apos"] = "'", ["nbsp"] = " ",
        ["mdash"] = "-", ["ndash"] = "-", ["ldquo"] = "\"", ["rdquo"] = "\"", ["lsquo"] = "'", ["rsquo"] = "'",
        ["hellip"] = "...", ["rarr"] = "->", ["larr"] = "<-", ["times"] = "x", ["le"] = "<=", ["ge"] = ">=", ["ne"] = "<>",
        ["plusmn"] = "+/-", ["middot"] = ".", ["copy"] = "(c)", ["eacute"] = "e", ["uuml"] = "u", ["ouml"] = "o", ["pi"] = "pi", ["zwsp"] = "",
    };

    [GeneratedRegex(@"<!\[CDATA\[(.*?)\]\]>", RegexOptions.Singleline)]
    private static partial Regex CData();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();

    // A cross-reference (<xref linkend="..."/>) has no text of its own. It is kept as a marker until the sentence is cut, so a
    // sentence that only pointed somewhere ("...; see <xref/>") can be cleaned instead of left ending in "see".
    private const char Reference = '\u0001';

    [GeneratedRegex(@"<xref\b[^>]*>")]
    private static partial Regex Xref();

    private const string Pointers = @"\u0001+(?:\s*(?:,|and|or)\s*\u0001+)*";

    [GeneratedRegex(@"\s*\(\s*[Ss]ee(?:\s+also)?\s+" + Pointers + @"\s*\)|\s*[;,]?\s*\b[Ss]ee(?:\s+also)?\s+" + Pointers)]
    private static partial Regex SeePointer();

    [GeneratedRegex(@"(?:\s+(?:in|at|to|of|and|or|for|by|with|as|the|a|an|from|on|see|also))+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DanglingWord();

    [GeneratedRegex(@"\(\s*\)")]
    private static partial Regex EmptyParentheses();

    [GeneratedRegex(@"&(#\d+|#x[0-9a-fA-F]+|[A-Za-z][A-Za-z0-9.\-]*);")]
    private static partial Regex Entity();

    [GeneratedRegex(@"\s+")]
    private static partial Regex White();

    /// <summary>Text of the fragment with tags removed and entities decoded; white space is left as it is.</summary>
    public static string Strip(string fragment)
    {
        if (string.IsNullOrEmpty(fragment)) return string.Empty;
        var text = CData().Replace(fragment, m => WebUtility.HtmlEncode(m.Groups[1].Value));
        text = Comment().Replace(text, string.Empty);
        text = Xref().Replace(text, Reference.ToString());
        text = Tag().Replace(text, string.Empty);
        return Entity().Replace(text, Decode);
    }

    /// <summary>
    /// Like <see cref="Strip"/>, with every run of white space made one space and the ends trimmed. Cross-references are removed,
    /// together with the "see" that introduced them.
    /// </summary>
    public static string Plain(string fragment) => Clean(Collapse(Strip(fragment)));

    private static string Collapse(string stripped) => White().Replace(stripped, " ").Trim();

    /// <summary>Removes the cross-reference markers and what only existed to introduce them; text without a marker is returned as it is.</summary>
    private static string Clean(string text)
    {
        if (text.IndexOf(Reference) < 0) return text;
        text = SeePointer().Replace(text, string.Empty);
        text = text.Replace(Reference.ToString(), string.Empty);
        text = EmptyParentheses().Replace(text, string.Empty);
        text = DanglingWord().Replace(White().Replace(text, " ").Trim(), string.Empty);
        return text.Trim().TrimEnd(',', ';', ':').Trim();
    }

    /// <summary>The first sentence of <paramref name="text"/> without its closing full stop, at most 240 characters.</summary>
    public static string FirstSentence(string text) => Sentences(text).FirstOrDefault() ?? string.Empty;

    /// <summary>
    /// The sentences of <paramref name="text"/> (each ends at a full stop followed by a space or the end), without the full stop.
    /// A full stop after an abbreviation such as "e.g." does not end one. Empty sentences are skipped.
    /// </summary>
    public static IEnumerable<string> Sentences(string text)
    {
        var collapsed = Collapse(Strip(text));
        var start = 0;
        for (var i = 0; i < collapsed.Length; i++)
        {
            var c = collapsed[i];
            if (c != '.' && c != '?' && c != '!') continue;
            if (i + 1 < collapsed.Length && collapsed[i + 1] != ' ') continue;
            if (c == '.' && IsAbbreviation(collapsed, i)) continue;
            // A full stop is left out of the sentence; a question or exclamation mark is part of it.
            var sentence = Finish(collapsed.Substring(start, c == '.' ? i - start : i + 1 - start));
            if (sentence.Length > 0) yield return sentence;
            start = i + 1;
        }
        if (start < collapsed.Length)
        {
            var tail = Finish(collapsed.Substring(start));
            if (tail.Length > 0) yield return tail;
        }
    }

    private static string Finish(string sentence)
    {
        var clean = Clean(sentence.Trim());
        return clean.Length > 240 ? clean.Substring(0, 237).TrimEnd() + "..." : clean;
    }

    private static readonly string[] Abbreviations = { "e.g", "i.e", "etc", "vs", "cf", "approx", "incl", "resp" };

    // "e.g." and "i.e." contain full stops of their own; the word before the stop is looked at.
    private static bool IsAbbreviation(string text, int stop)
    {
        var from = stop;
        while (from > 0 && (char.IsLetter(text[from - 1]) || text[from - 1] == '.')) from--;
        var word = text.Substring(from, stop - from).ToLowerInvariant();
        return Abbreviations.Contains(word);
    }

    private static string Decode(Match match)
    {
        var name = match.Groups[1].Value;
        if (name[0] == '#')
        {
            var number = name[1] == 'x'
                ? int.TryParse(name.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex) ? hex : -1
                : int.TryParse(name.AsSpan(1), out var dec) ? dec : -1;
            return number is > 0 and < 0x110000 ? char.ConvertFromUtf32(number) : string.Empty;
        }
        return Entities.TryGetValue(name, out var text) ? text : string.Empty;
    }

    /// <summary>
    /// The multi-line text of a <c>&lt;synopsis&gt;</c>: tags removed and entities decoded like <see cref="Strip"/>, trailing
    /// blanks removed, the common indentation removed and blank lines at the ends dropped. Line breaks and the indentation
    /// inside the text are kept, because that is how the documentation lays the syntax out.
    /// </summary>
    public static string Block(string fragment)
    {
        var lines = Strip(fragment).Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return string.Empty;

        var indent = lines.Where(l => l.Length > 0).Min(l => l.Length - l.TrimStart().Length);
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) builder.Append('\n');
            builder.Append(lines[i].Length >= indent ? lines[i].Substring(indent) : lines[i]);
        }
        return builder.ToString();
    }
}
