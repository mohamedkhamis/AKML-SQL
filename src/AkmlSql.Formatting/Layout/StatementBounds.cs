using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// Spec 047 (FMT-002): where a statement ends, for the rules that measure "the rest of this statement" or "the rest of
/// this list" before collapsing it onto one line. A scan that stopped only at a semicolon, GO, a closing parenthesis or
/// the next clause keyword ran on through statements written without semicolons and joined them:
/// "END WHILE @a > 0 BEGIN", "END CATCH IF @a > 2", "SELECT 1 DROP TABLE dbo.t PRINT 1".
/// </summary>
internal static class StatementBounds
{
    /// <summary>
    /// True when <paramref name="index"/> is past the end of a statement that started before it: the parse tree starts
    /// another statement there (<see cref="LayoutNode.StartsStatement"/>), or it is the END or ELSE of the block or IF
    /// the statement sits in. A CASE expression's END and ELSE are not: <paramref name="openCases"/> counts the CASE
    /// expressions the scan has entered and not yet left. Call it for every node of the scan, at parenthesis depth 0.
    /// </summary>
    public static bool EndsBefore(List<LayoutNode> nodes, int index, ref int openCases)
    {
        var node = nodes[index];
        if (node.StartsStatement)
            return true;

        switch (node.TokenType)
        {
            case TSqlTokenType.Case:
                openCases++;
                return false;
            case TSqlTokenType.End:
                if (openCases == 0) return true;
                openCases--;
                return false;
            case TSqlTokenType.Else:
                return openCases == 0;
            default:
                return false;
        }
    }
}
