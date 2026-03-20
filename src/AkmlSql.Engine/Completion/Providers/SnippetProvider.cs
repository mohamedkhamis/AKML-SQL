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

    private static readonly SnippetDefinition[] BuiltInSnippets =
    [
        new("ssf", "SELECT * FROM",
            "SELECT *\nFROM $1\nWHERE $2"),

        new("sel", "SELECT columns",
            "SELECT $1\nFROM $2\nWHERE $3\nORDER BY $4"),

        new("ins", "INSERT INTO",
            "INSERT INTO $1 ($2)\nVALUES ($3)"),

        new("upd", "UPDATE SET",
            "UPDATE $1\nSET $2 = $3\nWHERE $4"),

        new("del", "DELETE FROM",
            "DELETE FROM $1\nWHERE $2"),

        new("cte", "Common Table Expression",
            ";WITH $1 AS (\n    SELECT $2\n    FROM $3\n    WHERE $4\n)\nSELECT *\nFROM $1")
    ];

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Snippets are available at the start of statements or in Unknown clause context
        // (i.e., when the user hasn't started a clause yet)
        return context.ClauseType == ClauseType.Unknown
            && !context.PrecedingDot
            && !context.InComment
            && !context.InString;
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
