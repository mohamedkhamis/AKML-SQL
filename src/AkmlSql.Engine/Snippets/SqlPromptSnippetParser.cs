using System.Xml.Linq;
using AkmlSql.Engine.Snippets.Models;
using Serilog;

namespace AkmlSql.Engine.Snippets;

/// <summary>
/// Spec 030 T042/T043 (FR-032, R7) — tolerant importer for Red Gate SQL Prompt
/// <c>.sqlpromptsnippet</c> files, mapping SQL Prompt placeholder tokens to their AKML equivalents.
///
/// <para><b>Assumed XML schema</b> (the EXACT SQL Prompt schema is uncertain — validate this against a
/// real <c>.sqlpromptsnippet</c> file later). SQL Prompt exports use the Visual Studio CodeSnippet
/// schema, typically with the default namespace
/// <c>http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet</c>. We parse two shapes with the
/// same code path by matching element <see cref="XName.LocalName"/> over <i>descendants</i> (so the
/// default xmlns is a non-issue and nesting depth does not matter):</para>
///
/// <para><b>Nested (real SQL Prompt / VS CodeSnippet):</b></para>
/// <code>
/// &lt;CodeSnippets xmlns="http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet"&gt;
///   &lt;CodeSnippet&gt;
///     &lt;Header&gt;
///       &lt;Title&gt;Select all from table&lt;/Title&gt;
///       &lt;Shortcut&gt;ssf&lt;/Shortcut&gt;
///       &lt;Description&gt;SELECT * FROM ...&lt;/Description&gt;
///       &lt;Author&gt;Red Gate&lt;/Author&gt;
///       &lt;SnippetTypes&gt;&lt;SnippetType&gt;Expansion&lt;/SnippetType&gt;&lt;/SnippetTypes&gt;
///     &lt;/Header&gt;
///     &lt;Snippet&gt;
///       &lt;Declarations&gt;
///         &lt;Literal&gt;
///           &lt;ID&gt;tableName&lt;/ID&gt;
///           &lt;ToolTip&gt;Target table&lt;/ToolTip&gt;
///           &lt;Default&gt;dbo.MyTable&lt;/Default&gt;
///         &lt;/Literal&gt;
///       &lt;/Declarations&gt;
///       &lt;Code Language="SQL"&gt;&lt;![CDATA[SELECT * FROM $tableName$ WHERE id = $DBNAME$ $CURSOR$]]&gt;&lt;/Code&gt;
///     &lt;/Snippet&gt;
///   &lt;/CodeSnippet&gt;
/// &lt;/CodeSnippets&gt;
/// </code>
///
/// <para><b>Flat (tolerant fallback):</b></para>
/// <code>
/// &lt;Snippet&gt;
///   &lt;Title&gt;quick&lt;/Title&gt;
///   &lt;Description&gt;...&lt;/Description&gt;
///   &lt;Code&gt;SELECT $CURSOR$&lt;/Code&gt;
/// &lt;/Snippet&gt;
/// </code>
///
/// <para><b>Token mapping</b> (SQL Prompt → AKML): <c>$DBNAME$</c>→<c>$DATABASE$</c>,
/// <c>$PASTE$</c>→<c>$CLIPBOARD$</c>, <c>$SELECTION_START$</c>/<c>$SELECTIONSTART$</c>→<c>$SELECTIONSTART$</c>,
/// <c>$SELECTION_END$</c>/<c>$SELECTIONEND$</c>→<c>$SELECTIONEND$</c>, <c>$CURSOR$</c> left as-is. Any
/// other <c>$...$</c> token (including declared <c>Literal</c> variables) is preserved untouched.</para>
///
/// <para>Note: spec 004's import-mapping contract said to <i>drop</i> <c>$SELECTIONSTART$</c>/
/// <c>$SELECTIONEND$</c>. We instead preserve them because spec 030 (T040/T047) made the engine's
/// <c>HandleExpand</c> treat those as live selection-range markers — so preserving is strictly more
/// correct now than it was when the 004 contract was written.</para>
/// </summary>
internal static class SqlPromptSnippetParser
{
    /// <summary>
    /// Parses SQL Prompt snippet XML into AKML <see cref="Snippet"/> objects. Returns an EMPTY list
    /// on unparseable input (mirrors <c>TryParseAkmlSnippet</c>'s defensive null-on-failure), so the
    /// caller's fallback chain can keep probing other formats.
    /// </summary>
    public static List<Snippet> ParseXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            Log.Debug(ex, "Failed to parse content as SqlPromptSnippet (SQL Prompt XML) format");
            return [];
        }

        if (doc.Root == null)
            return [];

        // Match the snippet container by LocalName so the default CodeSnippet xmlns is irrelevant and
        // both <CodeSnippets>/<CodeSnippet> nesting and a single flat root resolve through one path.
        var snippetElements = doc.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "CodeSnippet", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Flat shape (no <CodeSnippet> wrapper): treat the document root itself as one snippet.
        if (snippetElements.Count == 0)
            snippetElements = [doc.Root];

        var results = new List<Snippet>();
        foreach (var el in snippetElements)
        {
            var snippet = BuildSnippet(el);
            if (snippet != null)
                results.Add(snippet);
        }

        return results;
    }

    private static Snippet? BuildSnippet(XElement root)
    {
        var bodyText = FirstDescendantValue(root, "Code") ?? FirstDescendantValue(root, "Body");
        var title = FirstDescendantValue(root, "Title") ?? FirstDescendantValue(root, "Name");
        var shortcut = FirstDescendantValue(root, "Shortcut") ?? FirstDescendantValue(root, "Shortcode");
        var description = FirstDescendantValue(root, "Description") ?? string.Empty;
        var author = FirstDescendantValue(root, "Author") ?? string.Empty;

        // A snippet with neither a body nor any identifying text is noise — skip it.
        if (string.IsNullOrWhiteSpace(bodyText) && string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(shortcut))
            return null;

        var variables = ParseVariables(root);

        var mappedBody = MapTokens(bodyText ?? string.Empty);
        var bodyLines = SplitBody(mappedBody);

        var shortcode = DeriveShortcode(shortcut, title, bodyText);

        var surroundsWith = root.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "SnippetType", StringComparison.OrdinalIgnoreCase))
            .Any(e => e.Value.Trim().IndexOf("SurroundsWith", StringComparison.OrdinalIgnoreCase) >= 0);

        return new Snippet
        {
            Metadata = new SnippetMetadata
            {
                Id = Guid.NewGuid().ToString(),
                Shortcode = shortcode,
                Name = string.IsNullOrWhiteSpace(title) ? shortcode : title!.Trim(),
                Description = description.Trim(),
                Author = string.IsNullOrWhiteSpace(author) ? "Imported" : author.Trim(),
                Category = "Custom",
                Tags = [],
                Context = ["global"],
                SurroundsWith = surroundsWith
            },
            Variables = variables.ToArray(),
            Body = bodyLines
        };
    }

    private static List<SnippetVariable> ParseVariables(XElement root)
    {
        var variables = new List<SnippetVariable>();
        var literals = root.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "Literal", StringComparison.OrdinalIgnoreCase));

        foreach (var literal in literals)
        {
            var name = FirstDescendantValue(literal, "ID");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            variables.Add(new SnippetVariable
            {
                Name = name.Trim(),
                Default = (FirstDescendantValue(literal, "Default") ?? string.Empty).Trim(),
                Tooltip = (FirstDescendantValue(literal, "ToolTip") ?? string.Empty).Trim()
                // schemaAware intentionally not set — schema-awareness is AKML-specific.
            });
        }

        return variables;
    }

    /// <summary>Maps SQL Prompt placeholder tokens to their AKML equivalents (case-insensitive).</summary>
    private static string MapTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Order matters: rename the underscore variants to the canonical AKML form first.
        return text
            .Replace("$DBNAME$", "$DATABASE$", StringComparison.OrdinalIgnoreCase)
            .Replace("$PASTE$", "$CLIPBOARD$", StringComparison.OrdinalIgnoreCase)
            .Replace("$SELECTION_START$", "$SELECTIONSTART$", StringComparison.OrdinalIgnoreCase)
            .Replace("$SELECTION_END$", "$SELECTIONEND$", StringComparison.OrdinalIgnoreCase);
        // $SELECTIONSTART$/$SELECTIONEND$ already match AKML; $CURSOR$ and all other $...$ tokens
        // (including declared Literal variables) are preserved untouched.
    }

    private static string[] SplitBody(string body)
    {
        if (string.IsNullOrEmpty(body))
            return [];

        // Normalise CRLF/CR → LF, then split. Trim a single leading/trailing blank line that CDATA
        // formatting commonly introduces, but keep interior blank lines intact.
        var normalised = body.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalised.Split('\n').ToList();

        while (lines.Count > 0 && lines[0].Trim().Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return lines.ToArray();
    }

    /// <summary>
    /// Derives a non-empty shortcode: explicit Shortcut → first word of Title → first word of body.
    /// <c>HandleImport</c> rejects blank-shortcode snippets, so this MUST yield a value.
    /// </summary>
    private static string DeriveShortcode(string? shortcut, string? title, string? body)
    {
        if (!string.IsNullOrWhiteSpace(shortcut))
            return shortcut.Trim();

        var fromTitle = FirstWord(title);
        if (!string.IsNullOrEmpty(fromTitle))
            return fromTitle;

        var fromBody = FirstWord(body);
        if (!string.IsNullOrEmpty(fromBody))
            return fromBody;

        return "imported";
    }

    private static string FirstWord(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        foreach (var token in text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return string.Empty;
    }

    /// <summary>
    /// Returns the trimmed value of the first descendant element whose <see cref="XName.LocalName"/>
    /// matches (case-insensitive), or <c>null</c> if none exists. Namespace-agnostic by design.
    /// </summary>
    private static string? FirstDescendantValue(XElement root, string localName)
    {
        var match = root.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        return match?.Value;
    }
}
