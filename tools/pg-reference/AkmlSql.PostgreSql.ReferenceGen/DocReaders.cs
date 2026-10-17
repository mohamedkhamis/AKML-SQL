using System.Text.RegularExpressions;

namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>One page of the SQL command reference (<c>doc/src/sgml/ref/*.sgml</c>).</summary>
/// <param name="Id">The <c>refentry</c> id, for example <c>sql-select</c>; also the page name on the web site.</param>
/// <param name="Names">Every <c>refname</c>: <c>SELECT</c>, <c>TABLE</c> and <c>WITH</c> share one page.</param>
public sealed record RefPage(string Id, IReadOnlyList<string> Names, string Purpose, string Synopsis);

/// <summary>Reads a command reference page: its id, names, one-line purpose and first synopsis.</summary>
public static partial class RefPageReader
{
    [GeneratedRegex(@"<refentry\s+id=""([^""]+)""")]
    private static partial Regex RefEntry();

    [GeneratedRegex(@"<refname>(.*?)</refname>", RegexOptions.Singleline)]
    private static partial Regex RefName();

    [GeneratedRegex(@"<refpurpose>(.*?)</refpurpose>", RegexOptions.Singleline)]
    private static partial Regex Purpose();

    [GeneratedRegex(@"<synopsis>(.*?)</synopsis>", RegexOptions.Singleline)]
    private static partial Regex Synopsis();

    /// <summary>The page, or null when the text has no <c>refentry</c>.</summary>
    public static RefPage? Read(string sgml)
    {
        var entry = RefEntry().Match(sgml);
        if (!entry.Success) return null;

        var names = RefName().Matches(sgml).Select(m => SgmlText.Plain(m.Groups[1].Value)).Where(n => n.Length > 0).ToList();
        if (names.Count == 0) return null;

        var purpose = Purpose().Match(sgml);
        var synopsis = Synopsis().Match(sgml);
        return new RefPage(
            entry.Groups[1].Value,
            names,
            purpose.Success ? SgmlText.Plain(purpose.Groups[1].Value) : string.Empty,
            synopsis.Success ? SgmlText.Block(synopsis.Groups[1].Value) : string.Empty);
    }
}

/// <summary>A function or operator signature from a table of <c>func.sgml</c>.</summary>
/// <param name="Name">The function name as the documentation writes it, or the operator.</param>
/// <param name="Arguments">The text between the parentheses, tags removed (empty for an operator).</param>
/// <param name="FromProse">Read from a synopsis block and the paragraph after it, not from a table row: the description is a best guess.</param>
public sealed record DocFunction(string Name, bool IsOperator, string SectionId, string Signature, string Arguments, string Returns, string Description, bool FromProse = false);

/// <summary>
/// Reads <c>func.sgml</c>: each table row is an <c>&lt;entry role="func_table_entry"&gt;</c> holding one or more
/// <c>&lt;para role="func_signature"&gt;</c> (the function or operator, its arguments and <c>&lt;returnvalue&gt;</c>) and then a
/// description paragraph. The page a row belongs to is the enclosing <c>&lt;sect1 id="..."&gt;</c>.
/// </summary>
public static partial class FuncSectionIndex
{
    [GeneratedRegex(@"<sect1\s+id=""([^""]+)""")]
    private static partial Regex Sect1();

    // Some rows carry an id and an xreflabel before the role: <entry id="format-type" xreflabel="format_type" role="func_table_entry">.
    [GeneratedRegex(@"<entry\b[^>]*\brole=""func_table_entry""[^>]*>(.*?)</entry>", RegexOptions.Singleline)]
    private static partial Regex Entry();

    [GeneratedRegex(@"<synopsis>(.*?)</synopsis>", RegexOptions.Singleline)]
    private static partial Regex Synopsis();

    // Code blocks between the paragraphs that follow a synopsis; they are not prose and must not become a summary.
    [GeneratedRegex(@"<(synopsis|programlisting|screen)>.*?</(?:synopsis|programlisting|screen)>", RegexOptions.Singleline)]
    private static partial Regex Examples();

    // A line that starts with a name and an opening parenthesis: COALESCE(value , ...), pg_sleep ( double precision ), JSON_EXISTS (
    [GeneratedRegex(@"^\s*([A-Za-z_][A-Za-z0-9_]+)\s*\((.*)$")]
    private static partial Regex CallLine();

    [GeneratedRegex(@"<para\s+role=""func_signature"">(.*?)</para>", RegexOptions.Singleline)]
    private static partial Regex SignaturePara();

    [GeneratedRegex(@"<para>(.*?)</para>", RegexOptions.Singleline)]
    private static partial Regex DescriptionPara();

    [GeneratedRegex(@"<indexterm.*?</indexterm>", RegexOptions.Singleline)]
    private static partial Regex IndexTerm();

    [GeneratedRegex(@"<function>(.*?)</function>", RegexOptions.Singleline)]
    private static partial Regex FunctionTag();

    [GeneratedRegex(@"<returnvalue>(.*?)</returnvalue>", RegexOptions.Singleline)]
    private static partial Regex ReturnValue();

    [GeneratedRegex(@"<literal>(.*?)</literal>", RegexOptions.Singleline)]
    private static partial Regex Literal();

    public static IReadOnlyList<DocFunction> Read(string funcSgml)
    {
        // Section starts, so a row can find the page it is on.
        var sections = Sect1().Matches(funcSgml).Select(m => (m.Index, Id: m.Groups[1].Value)).ToList();
        string SectionAt(int index)
        {
            var id = "functions";
            foreach (var (start, sectionId) in sections)
            {
                if (start > index) break;
                id = sectionId;
            }
            return id;
        }

        var result = new List<DocFunction>();
        foreach (Match entry in Entry().Matches(funcSgml))
        {
            var body = entry.Groups[1].Value;
            var signatures = SignaturePara().Matches(body).Select(m => m.Groups[1].Value).ToList();
            if (signatures.Count == 0) continue;

            // The description is the first paragraph that is not a signature.
            var withoutSignatures = SignaturePara().Replace(body, string.Empty);
            var description = DescriptionPara().Match(withoutSignatures);
            var text = description.Success ? SgmlText.FirstSentence(description.Groups[1].Value) : string.Empty;
            var section = SectionAt(entry.Index);

            foreach (var raw in signatures)
            {
                var parsed = ParseSignature(raw, section, text);
                if (parsed != null) result.Add(parsed);
            }
        }

        // Functions that look like syntax (COALESCE, GREATEST, pg_sleep, JSON_EXISTS, ...) are written as a <synopsis> block, not as a
        // table row. A name already found in a table is not read again.
        var known = new HashSet<string>(result.Where(r => !r.IsOperator).Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        foreach (Match block in Synopsis().Matches(funcSgml))
        {
            var lines = SgmlText.Block(block.Groups[1].Value).Split('\n');
            var following = Examples().Replace(
                funcSgml.Substring(block.Index + block.Length, Math.Min(6000, funcSgml.Length - block.Index - block.Length)), string.Empty);
            var section = SectionAt(block.Index);
            foreach (var line in lines)
            {
                var call = CallLine().Match(line);
                if (!call.Success) continue;
                var name = call.Groups[1].Value;
                if (!known.Add(name)) continue;

                var rest = call.Groups[2].Value;
                var close = rest.LastIndexOf(')');
                var arguments = (close >= 0 ? rest.Substring(0, close) : rest).Trim();
                var returns = close >= 0 && close + 1 < rest.Length ? rest.Substring(close + 1).Trim() : string.Empty;
                if (returns.Length > 40 || returns.Contains(' ') && !returns.StartsWith("setof ", StringComparison.Ordinal)) returns = string.Empty;
                result.Add(new DocFunction(name, false, section, line.Trim(), arguments, returns, SentenceAbout(following, name), FromProse: true));
            }
        }
        return result;
    }

    /// <summary>
    /// The function names and operators the manual shows, found by a plainer scan than <see cref="Read"/>: every
    /// <c>func_signature</c> paragraph wherever it is, and every call-shaped line of a synopsis. This is the denominator of the
    /// coverage figures, so a row the reader skips shows up as a gap instead of passing unnoticed.
    /// </summary>
    public static (HashSet<string> Functions, HashSet<string> Operators) DocumentedNames(string funcSgml)
    {
        var functions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match para in SignaturePara().Matches(funcSgml))
        {
            var cleaned = IndexTerm().Replace(para.Groups[1].Value, string.Empty);
            var returns = ReturnValue().Match(cleaned);
            var before = returns.Success ? cleaned.Substring(0, returns.Index) : cleaned;
            var function = FunctionTag().Match(before);
            if (function.Success)
            {
                var name = SgmlText.Plain(function.Groups[1].Value);
                if (name.Length > 0) functions.Add(name);
                continue;
            }
            var literal = Literal().Match(before);
            var op = literal.Success ? SgmlText.Plain(literal.Groups[1].Value) : string.Empty;
            if (op.Length > 0) operators.Add(op);
        }

        foreach (Match block in Synopsis().Matches(funcSgml))
            foreach (var line in SgmlText.Block(block.Groups[1].Value).Split((char)10))
                if (CallLine().Match(line) is { Success: true } call) functions.Add(call.Groups[1].Value);

        return (functions, operators);
    }

    // The first of the first few sentences that mentions the name; the first sentence when none does.
    private static string SentenceAbout(string text, string name)
    {
        var sentences = SgmlText.Sentences(text).Take(6).ToList();
        return sentences.FirstOrDefault(s => Regex.IsMatch(s, @"\b" + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase))
            ?? sentences.FirstOrDefault() ?? string.Empty;
    }

    private static DocFunction? ParseSignature(string raw, string section, string description)
    {
        var cleaned = IndexTerm().Replace(raw, string.Empty);
        var returns = ReturnValue().Match(cleaned);
        var returnsText = returns.Success ? SgmlText.Plain(returns.Groups[1].Value) : string.Empty;
        var beforeReturns = returns.Success ? cleaned.Substring(0, returns.Index) : cleaned;

        var function = FunctionTag().Match(beforeReturns);
        if (function.Success)
        {
            var name = SgmlText.Plain(function.Groups[1].Value);
            if (name.Length == 0) return null;
            var afterName = SgmlText.Plain(beforeReturns.Substring(function.Index + function.Length));
            var open = afterName.IndexOf('(');
            var close = afterName.LastIndexOf(')');
            var arguments = open >= 0 && close > open ? afterName.Substring(open + 1, close - open - 1).Trim() : string.Empty;
            return new DocFunction(name, false, section, SgmlText.Plain(beforeReturns), arguments, returnsText, description);
        }

        var literal = Literal().Match(beforeReturns);
        if (!literal.Success) return null;
        var op = SgmlText.Plain(literal.Groups[1].Value);
        return op.Length == 0 ? null : new DocFunction(op, true, section, SgmlText.Plain(beforeReturns), string.Empty, returnsText, description);
    }
}

/// <summary>One row of the data types table of <c>datatype.sgml</c> (or of the pseudo-types table).</summary>
/// <param name="Name">The type name with any length or precision part removed: <c>bit varying</c>, <c>timestamp with time zone</c>.</param>
public sealed record DocType(string Name, IReadOnlyList<string> Aliases, string Description, string SectionId, bool IsPseudo);

/// <summary>
/// Reads the data types table (<c>&lt;table id="datatype-table"&gt;</c>: name, aliases, description) and the pseudo-types table. The
/// page of a type is the <c>sect1</c> of <c>datatype.sgml</c> that mentions it most.
/// </summary>
public static partial class DatatypeSectionIndex
{
    [GeneratedRegex(@"<table\s+id=""(datatype-table|datatype-pseudotypes-table)"">(.*?)</table>", RegexOptions.Singleline)]
    private static partial Regex Table();

    [GeneratedRegex(@"<row>(.*?)</row>", RegexOptions.Singleline)]
    private static partial Regex Row();

    [GeneratedRegex(@"<entry>(.*?)</entry>", RegexOptions.Singleline)]
    private static partial Regex Cell();

    [GeneratedRegex(@"<type>(.*?)</type>", RegexOptions.Singleline)]
    private static partial Regex TypeTag();

    [GeneratedRegex(@"<sect1\s+id=""([^""]+)""")]
    private static partial Regex Sect1();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex Optional();

    [GeneratedRegex(@"<table[\s>].*?</table>", RegexOptions.Singleline)]
    private static partial Regex AnyTable();

    public static IReadOnlyList<DocType> Read(string datatypeSgml)
    {
        var sections = Sect1().Matches(datatypeSgml).Select(m => (m.Index, Id: m.Groups[1].Value)).ToList();
        var result = new List<DocType>();

        foreach (Match table in Table().Matches(datatypeSgml))
        {
            var pseudo = table.Groups[1].Value != "datatype-table";
            foreach (Match row in Row().Matches(table.Groups[2].Value))
            {
                var cells = Cell().Matches(row.Groups[1].Value).Select(m => m.Groups[1].Value).ToList();
                if (cells.Count < 2) continue;

                var names = TypeTag().Matches(cells[0]).Select(m => Normalise(m.Groups[1].Value)).Where(n => n.Length > 0).ToList();
                if (names.Count == 0) continue;
                var aliases = cells.Count >= 3
                    ? TypeTag().Matches(cells[1]).Select(m => Normalise(m.Groups[1].Value)).Where(n => n.Length > 0).ToList()
                    : new List<string>();
                var description = SgmlText.Plain(cells[^1]);

                var section = BestSection(datatypeSgml, sections, names[0], pseudo);
                result.Add(new DocType(names[0], aliases, description, section, pseudo));
            }
        }
        return result;
    }

    /// <summary>
    /// <c>bit varying [ (n) ]</c> becomes <c>bit varying</c>; <c>timestamp [ (p) ] with time zone</c> becomes
    /// <c>timestamp with time zone</c>. Optional parts (square brackets, parentheses) are removed and white space collapsed.
    /// </summary>
    public static string Normalise(string typeText)
    {
        var plain = SgmlText.Plain(typeText);
        string previous;
        do
        {
            previous = plain;
            plain = Optional().Replace(plain, " ");
        }
        while (plain != previous);
        return Regex.Replace(plain, @"\s+", " ").Trim();
    }

    // The sect1 with the most <type>name</type> mentions; the earliest wins a tie. The pseudo-types have their own page.
    private static string BestSection(string text, List<(int Index, string Id)> sections, string name, bool pseudo)
    {
        if (pseudo) return "datatype-pseudo";
        var best = "datatype";
        var bestCount = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var from = sections[i].Index;
            var to = i + 1 < sections.Count ? sections[i + 1].Index : text.Length;
            var count = 0;
            // The overview table lists every type once; it says nothing about which page explains one.
            foreach (Match m in TypeTag().Matches(AnyTable().Replace(text.Substring(from, to - from), string.Empty)))
                if (string.Equals(Normalise(m.Groups[1].Value), name, StringComparison.OrdinalIgnoreCase)) count++;
            if (count > bestCount) { best = sections[i].Id; bestCount = count; }
        }
        return best;
    }
}
