using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Productivity;

/// <summary>
/// Spec 030 T066 / FR-022 — the pure transform behind "Script object as → ALTER".
///
/// Rewrites a programmable object's fetched definition (<c>CREATE …</c> from
/// <c>sys.sql_modules</c>) into its <c>ALTER …</c> form by replacing only the leading
/// object-level <c>CREATE</c> keyword. The rewrite is token-aware (it tokenises the
/// definition rather than running a regex over raw text), so it:
/// <list type="bullet">
///   <item>rewrites only the FIRST <c>CREATE</c> — a <c>CREATE TABLE #t</c> inside the body is left alone;</item>
///   <item>collapses <c>CREATE OR ALTER …</c> to <c>ALTER …</c> (dropping the redundant <c>CREATE OR</c>);</item>
///   <item>tolerates leading comments / whitespace before the keyword;</item>
///   <item>is case-insensitive on the keyword.</item>
/// </list>
///
/// No live DB and no AST — the live definition fetch happens at the handler layer
/// (<see cref="ScriptAsHandler"/> via <see cref="Navigation.ObjectDefinitionService"/>), keeping
/// this transform deterministic and unit-testable.
/// </summary>
public static class ScriptAsAlterRewriter
{
    /// <summary>
    /// Converts a <c>CREATE</c> / <c>CREATE OR ALTER</c> object definition into its <c>ALTER</c>
    /// form. Returns <c>(false, null, error)</c> when the text has no leading <c>CREATE</c>
    /// (e.g. an encrypted module's placeholder comment, or a non-module definition).
    /// </summary>
    public static (bool Ok, string? Altered, string? Error) ToAlter(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return (false, null, "The object has no definition text to script as ALTER.");

        IList<TSqlParserToken> tokens;
        try { tokens = new TsqlParserService().GetTokenStream(definition); }
        catch { tokens = new List<TSqlParserToken>(); }

        int i = SkipTrivia(tokens, 0);
        if (i >= tokens.Count || tokens[i].TokenType != TSqlTokenType.Create)
            return (false, null,
                "The definition does not begin with CREATE, so it cannot be scripted as ALTER. " +
                "Only CREATE / CREATE OR ALTER programmable objects (procedures, views, functions, " +
                "triggers) are supported — an encrypted or non-module object has no scriptable definition.");

        var create = tokens[i];

        // CREATE OR ALTER … → ALTER …  : drop "CREATE OR " and keep the existing ALTER token verbatim.
        int j = SkipTrivia(tokens, i + 1);
        if (j < tokens.Count && tokens[j].TokenType == TSqlTokenType.Or)
        {
            int k = SkipTrivia(tokens, j + 1);
            if (k < tokens.Count && tokens[k].TokenType == TSqlTokenType.Alter)
            {
                int from = create.Offset;
                int to   = tokens[k].Offset;
                return (true, definition.Remove(from, to - from), null);
            }
        }

        // Plain CREATE … → ALTER …  : replace just the CREATE keyword token.
        var altered = definition.Remove(create.Offset, create.Text.Length).Insert(create.Offset, "ALTER");
        return (true, altered, null);
    }

    private static int SkipTrivia(IList<TSqlParserToken> tokens, int index)
    {
        while (index < tokens.Count && IsTrivia(tokens[index].TokenType))
            index++;
        return index;
    }

    private static bool IsTrivia(TSqlTokenType t) =>
        t == TSqlTokenType.WhiteSpace ||
        t == TSqlTokenType.SingleLineComment ||
        t == TSqlTokenType.MultilineComment;
}
