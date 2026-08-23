using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Spec 032 (C3, FR-016) — stored-procedure PARAMETER completion in EXEC argument positions.
/// Phase B loads parameters into the schema cache and <c>SignatureProvider</c> reads them for
/// signature help, but until spec 032 nothing emitted them as completion items. Scans the
/// token stream (attached by <c>CompletionEngine</c>) backwards for the owning EXEC/EXECUTE,
/// resolves the fully-typed procedure name against the cache, and offers its parameters —
/// excluding ones already supplied earlier in the argument list.
/// Items use <c>CompletionObjectType.Parameter</c> (never Snippet — SSMS hides/expands those).
/// </summary>
public class ParameterProvider : ICompletionProvider
{
    public string Name => "Parameter";

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        return cache != null && context.ClauseType == ClauseType.Exec;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (cache == null) yield break;

        var tokens = SmartGroupByContextExtensions.GetTokens(context);
        if (tokens == null) yield break;

        // Locate the EXEC/EXECUTE that owns the caret (statement-bounded by semicolons).
        int execIndex = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Offset >= context.CursorOffset) break;
            if (t.TokenType == TSqlTokenType.Semicolon) execIndex = -1;
            else if (t.TokenType is TSqlTokenType.Exec or TSqlTokenType.Execute) execIndex = i;
        }

        if (execIndex < 0) yield break;

        // Read the multi-part procedure name after EXEC.
        var parts = new List<string>();
        int j = SkipTrivia(tokens, execIndex + 1);
        int nameEnd = -1;
        while (j < tokens.Count &&
               tokens[j].TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
        {
            parts.Add(tokens[j].Text.Trim('[', ']', '"'));
            nameEnd = tokens[j].Offset + (tokens[j].Text?.Length ?? 0);

            int k = SkipTrivia(tokens, j + 1);
            if (k < tokens.Count && tokens[k].TokenType == TSqlTokenType.Dot)
            {
                j = SkipTrivia(tokens, k + 1);
            }
            else
            {
                break;
            }
        }

        // Parameters are offered only once the name is fully typed and the caret is past it
        // (argument position). While the caret is still inside the name, ObjectProvider's
        // proc-name completion owns the position.
        if (parts.Count == 0 || context.CursorOffset <= nameEnd) yield break;

        var procName = parts[parts.Count - 1];
        var schemaName = parts.Count >= 2 ? parts[parts.Count - 2] : "dbo";
        var proc = cache.FindObject(schemaName, procName);
        if (proc == null || proc.Parameters.Count == 0) yield break;

        // Parameters already supplied between the proc name and the caret are not offered
        // again. The token the user is currently typing (spanning the caret) doesn't count.
        var alreadySupplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tokens)
        {
            if (t.Offset <= nameEnd) continue;
            if (t.Offset >= context.CursorOffset) break;
            if (t.TokenType != TSqlTokenType.Variable || t.Text == null) continue;
            if (t.Offset + t.Text.Length >= context.CursorOffset) continue; // the partial being typed
            alreadySupplied.Add(t.Text);
        }

        // With a typed `@pre` prefix, narrow to prefix matches provider-side (SSMS-like):
        // parameter lists are short and exact — level-5 fuzzy ("@P" ⊆ "@NewPrice") would
        // resurface every parameter and defeat the narrowing the user is asking for.
        var prefix = context.PartialText.StartsWith("@") && context.PartialText.Length >= 2
            ? context.PartialText
            : null;

        foreach (var p in proc.Parameters)
        {
            if (string.IsNullOrEmpty(p.ParameterName)) continue;
            var name = p.ParameterName.StartsWith("@") ? p.ParameterName : "@" + p.ParameterName;
            if (alreadySupplied.Contains(name)) continue;
            if (prefix != null && !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            yield return new CompletionItem
            {
                DisplayText = name,
                InsertText = name,
                ObjectType = (int)CompletionObjectType.Parameter,
                SecondaryText = p.TypeName + (p.IsOutput ? " OUTPUT" : string.Empty),
                SourceObject = proc.FullName,
                SortPriority = 15, // above columns/tables — in EXEC args these ARE the ask
            };
        }
    }

    private static int SkipTrivia(IList<TSqlParserToken> tokens, int start)
    {
        while (start < tokens.Count &&
               tokens[start].TokenType is TSqlTokenType.WhiteSpace
                   or TSqlTokenType.SingleLineComment
                   or TSqlTokenType.MultilineComment)
        {
            start++;
        }

        return start;
    }
}
