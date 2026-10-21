using System.Text.Json;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.PostgreSql.Completion.Providers;

namespace AkmlSql.PostgreSql.Completion;

/// <summary>
/// Spec 042 (task T104, FR-066) - the snippets that ship for PostgreSQL: select, insert, update, delete, CTE, create table,
/// create index, create function, create view, transaction block, each in PostgreSQL syntax. They are the
/// <c>Snippets/*.akmlsnippet</c> files embedded in this assembly - the same format as the SQL Server pack, with a
/// <c>dialect</c> of <c>postgresql</c> - so the list the completion shows and the text the expansion inserts have one source.
/// <c>$CURSOR$</c> marks where the caret lands after expansion.
/// </summary>
public static class PgSnippetPack
{
    private static readonly Lazy<IReadOnlyList<PgSnippetInfo>> Pack = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<PgSnippetInfo> BuiltIn => Pack.Value;

    /// <summary>The raw text of each pack file, by shortcode, for the engine's snippet index.</summary>
    public static IReadOnlyDictionary<string, string> Files { get; } = ReadFiles();

    private static Dictionary<string, string> ReadFiles()
    {
        var assembly = typeof(PgSnippetPack).Assembly;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".akmlsnippet", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            files[name] = reader.ReadToEnd();
        }
        return files;
    }

    private static IReadOnlyList<PgSnippetInfo> Load()
    {
        var list = new List<PgSnippetInfo>();
        foreach (var text in Files.Values)
        {
            using var doc = JsonDocument.Parse(text);
            var meta = doc.RootElement.GetProperty("metadata");
            var body = string.Join("\n", doc.RootElement.GetProperty("body").EnumerateArray().Select(l => l.GetString() ?? string.Empty));
            list.Add(new PgSnippetInfo(meta.GetProperty("shortcode").GetString() ?? string.Empty, meta.GetProperty("description").GetString() ?? string.Empty, body));
        }
        return list.OrderBy(s => s.Shortcode, StringComparer.Ordinal).ToList();
    }
}

/// <summary>Spec 042 (task T100, FR-066) - snippet shortcodes where a statement can begin.</summary>
internal sealed class PgSnippetProvider : IPgCompletionProvider
{
    public void Collect(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        if (c.AfterDot || c.AfterOperand) return;
        if (c.Clause is not (PgClause.None or PgClause.StatementStart)) return;
        if (c.PrecedingWords.Count > 0) return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var snippet in input.Options.Snippets.Concat(PgSnippetPack.BuiltIn))
        {
            if (!seen.Add(snippet.Shortcode)) continue;
            items.Add(PgItems.Make(snippet.Shortcode, snippet.Body, CompletionObjectType.Snippet, snippet.Description, PgItems.Snippet, filter: snippet.Shortcode));
        }
    }
}
