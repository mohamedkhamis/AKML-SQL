using System.Text;
using System.Text.Json;

namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>
/// Spec 042 (T091) - writes the generator's four outputs. Everything is written with LF line endings and no byte order mark so a
/// run on any machine produces the same bytes, which is what makes the diff of the human-readable file reviewable.
/// </summary>
public static class ReferenceWriter
{
    public const string JsonPath = "src/AkmlSql.PostgreSql/Reference/postgresql-reference.json";
    public const string OmissionsPath = "src/AkmlSql.PostgreSql/Reference/omissions.json";
    public const string CoveragePath = "src/AkmlSql.PostgreSql/Reference/coverage.json";
    public const string MarkdownPath = "doc/postgresql-reference.md";

    public static string ToJson(ReferenceFile file) => Normalise(JsonSerializer.Serialize(file, Json.Options));

    public static string OmissionsJson(IEnumerable<Omission> omissions) =>
        Normalise(JsonSerializer.Serialize(
            omissions.OrderBy(o => o.Kind, StringComparer.Ordinal).ThenBy(o => o.Name, StringComparer.Ordinal).ToList(),
            Json.Options));

    public static string CoverageJson(Coverage coverage) => Normalise(JsonSerializer.Serialize(coverage, Json.Options));

    /// <summary>Writes all four files under <paramref name="repoRoot"/> and returns their paths.</summary>
    public static IReadOnlyList<string> WriteAll(string repoRoot, BuildResult result)
    {
        var written = new List<string>();

        void Write(string relative, string text)
        {
            var path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            written.Add(path);
        }

        Write(JsonPath, ToJson(result.File));
        Write(OmissionsPath, OmissionsJson(result.Omissions));
        Write(CoveragePath, CoverageJson(result.Coverage));
        Write(MarkdownPath, ToMarkdown(result.File, result.Coverage));
        return written;
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd('\n') + "\n";

    // ── doc/postgresql-reference.md ─────────────────────────────────────────────────────────────

    private static readonly (string Kind, string Title, string Blurb)[] Sections =
    {
        (EntryKinds.Command, "SQL commands", "One entry per command name; the synopsis is the first one on the command's reference page."),
        (EntryKinds.Keyword, "Key words", "The parser's key words and how each may be used as a name."),
        (EntryKinds.Type, "Data types", "Types from the manual's data types tables, with the names they are also known by."),
        (EntryKinds.Function, "Functions", "Functions the manual documents. Overloads are merged into one entry."),
        (EntryKinds.Aggregate, "Aggregate functions", "Functions that combine many rows into one value."),
        (EntryKinds.Window, "Window functions", "Functions that are used with OVER."),
        (EntryKinds.Procedure, "Procedures", "Routines called with CALL."),
        (EntryKinds.Operator, "Operators", "Operators the manual documents. Overloads are merged into one entry."),
        (EntryKinds.SqlState, "SQLSTATE codes", "Error codes reported by the server."),
        (EntryKinds.Lexical, "Lexical rules", "The shapes of names, constants, comments and other tokens."),
    };

    public static string ToMarkdown(ReferenceFile file, Coverage coverage)
    {
        var majors = file.GeneratedFrom.Tags.Select(Generator.MajorOf).Where(m => m > 0).OrderBy(m => m).ToList();
        var sb = new StringBuilder();
        sb.Append("# PostgreSQL reference\n\n");
        sb.Append("This file is generated. Do not edit it: change the generator in `tools/pg-reference/` and run it again, see its README.\n\n");
        sb.Append($"It describes PostgreSQL {file.Version} and says in which release each entry first appears, for releases {string.Join(", ", majors)}. ");
        sb.Append("Every entry links to the page of the official manual it was read from.\n\n");

        sb.Append("## Coverage\n\n| Kind | In the manual | In this reference |\n| --- | ---: | ---: |\n");
        void Row(string label, CoverageCount c) => sb.Append($"| {label} | {c.Documented} | {c.Included} |\n");
        Row("Commands", coverage.Commands);
        Row("Key words", coverage.Keywords);
        Row("Types", coverage.Types);
        Row("Functions", coverage.Functions);
        Row("Operators", coverage.Operators);
        Row("SQLSTATE codes", coverage.SqlState);
        sb.Append('\n');

        foreach (var (kind, title, blurb) in Sections)
        {
            var entries = file.Entries.Where(e => e.Kind == kind).ToList();
            if (entries.Count == 0) continue;
            sb.Append($"## {title}\n\n{blurb}\n\n");
            sb.Append("| Name | Summary | Since | Manual |\n| --- | --- | ---: | --- |\n");
            foreach (var e in entries)
            {
                var name = e.Aliases.Count > 0 && (kind == EntryKinds.Type || kind == EntryKinds.SqlState)
                    ? $"{Cell(e.Name)} ({string.Join(", ", e.Aliases.Select(Cell))})"
                    : Cell(e.Name);
                sb.Append($"| `{name.Replace("`", "'")}` | {Cell(e.Summary)} | {e.MinVersion} | [{Cell(e.Source.SectionId)}]({e.Url}) |\n");
            }
            sb.Append('\n');
        }
        return Normalise(sb.ToString());
    }

    // One table cell: no pipes, no line breaks.
    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
}
