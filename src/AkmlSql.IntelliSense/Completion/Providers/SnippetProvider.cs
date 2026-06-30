using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// T097: Provides built-in SQL snippet completions.
/// Snippets use shortcodes that expand to full SQL templates with $1/$2 tab-stop placeholders.
/// </summary>
public class SnippetProvider : ICompletionProvider
{
    public string Name => "Snippet";

    // Spec 030 T039/T041: shortcode + description drive the popup list. The InsertText body is a
    // display fallback only — committing a snippet now resolves the shipped .akmlsnippet pack by
    // shortcode through the engine (CompletionController → SnippetExpand), so these bodies are kept in
    // sync with src/AkmlSql.Engine/snippets/*.akmlsnippet (the $CURSOR$ marker, not $1/$2 tab-stops).
    private static readonly SnippetDefinition[] BuiltInSnippets =
    [
        new("ssf", "SELECT * FROM",
            "SELECT *\nFROM $CURSOR$"),

        new("sel", "SELECT columns",
            "SELECT $CURSOR$\nFROM \nWHERE \nORDER BY "),

        new("ins", "INSERT INTO",
            "INSERT INTO $CURSOR$ ()\nVALUES ()"),

        new("upd", "UPDATE SET",
            "UPDATE $CURSOR$\nSET \nWHERE "),

        new("del", "DELETE FROM",
            "DELETE FROM $CURSOR$\nWHERE "),

        new("cte", "Common Table Expression",
            ";WITH $CURSOR$ AS (\n    SELECT \n    FROM \n    WHERE \n)\nSELECT *\nFROM ")
    ];

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Snippets are available at the start of statements or in Unknown clause context
        // (i.e., when the user hasn't started a clause yet)
        return context is { ClauseType: ClauseType.Unknown, PrecedingDot: false, InComment: false, InString: false };
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        foreach (var snippet in BuiltInSnippets)
        {
            yield return new CompletionItem
            {
                DisplayText = snippet.Shortcode,
                InsertText = snippet.InsertText,
                ObjectType = (int)CompletionObjectType.Snippet,
                SecondaryText = snippet.Description,
                SourceObject = string.Empty,
                SortPriority = 500 // Snippets appear after schema objects
            };
        }
    }

    private sealed record SnippetDefinition(string Shortcode, string Description, string InsertText);
}
