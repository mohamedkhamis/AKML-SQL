using System.Collections.Generic;
using AkmlSql.Formatting.Layout;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Shared parenthesis-classification heuristics used by more than one rule set
/// (<see cref="ParenthesisRules"/> and <see cref="ControlFlowRules"/>). Only the DDL-object-name check
/// (<see cref="IsDdlObjectName"/>) is shared, so the two passes agree on "is this paren a DDL object's
/// column/parameter list". The function-name policy is intentionally NOT shared: each rule keeps its own
/// (e.g. <c>ParenthesisRules.IsFunctionNameToken</c> accepts COALESCE / CONVERT / NULLIF, while
/// <c>ControlFlowRules</c> stays Identifier-only).
/// </summary>
internal static class ParenHeuristics
{
    /// <summary>
    /// True when the identifier ending at <paramref name="nameEnd"/> is the (possibly multi-part) name
    /// of a DDL object — walking back over Identifier/QuotedIdentifier/Dot tokens lands on
    /// TABLE / PROCEDURE / FUNCTION / TRIGGER / VIEW. Such a name's paren is a column or parameter
    /// list owned by the DDL layout passes, not a function call.
    /// </summary>
    internal static bool IsDdlObjectName(List<LayoutNode> nodes, int nameEnd)
    {
        int k = nameEnd;
        while (k >= 0 && nodes[k].TokenType is TSqlTokenType.Identifier
            or TSqlTokenType.QuotedIdentifier or TSqlTokenType.Dot)
        {
            k--;
        }
        return k >= 0 && nodes[k].TokenType is TSqlTokenType.Table or TSqlTokenType.Procedure
            or TSqlTokenType.Function or TSqlTokenType.Trigger or TSqlTokenType.View;
    }
}
