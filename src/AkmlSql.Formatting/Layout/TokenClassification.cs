using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// Shared token-level classification used by both <c>LayoutEngine</c> (base spacing) and
/// <c>ListRules</c> (collapse re-spacing) so the unary-sign heuristic lives in exactly one place.
/// </summary>
internal static class TokenClassification
{
    /// <summary>
    /// True when a <c>Plus</c>/<c>Minus</c> token acts as a unary sign (negation / explicit
    /// positive) rather than a binary arithmetic operator. A sign is unary when the token before it
    /// (<paramref name="tokenBeforeSign"/>) does NOT end a value — i.e. it follows an operator,
    /// comma, open paren, keyword, or the start of the expression, as opposed to an identifier,
    /// literal, variable, or closing paren. Used to keep a sign hugging its operand (so <c>-1</c>
    /// stays <c>-1</c>, not <c>- 1</c>).
    /// <para>This is a deliberate token-level heuristic, not exhaustive: a binary minus after a
    /// value-producing token type not listed in <see cref="IsValueEndingToken"/> (or after
    /// <c>NULL</c> / a global variable) would be treated as unary — acceptable, as none occur in the
    /// parity corpus.</para>
    /// </summary>
    public static bool IsUnarySign(TSqlTokenType signType, TSqlTokenType? tokenBeforeSign)
    {
        if (signType is not (TSqlTokenType.Minus or TSqlTokenType.Plus))
            return false;
        return tokenBeforeSign is null || !IsValueEndingToken(tokenBeforeSign.Value);
    }

    /// <summary>
    /// True for token types that terminate a value/operand (so a following <c>-</c>/<c>+</c> is
    /// binary, not a unary sign): identifiers, the literal kinds, a variable, and a closing paren.
    /// </summary>
    public static bool IsValueEndingToken(TSqlTokenType type)
    {
        return type switch
        {
            TSqlTokenType.Identifier or
            TSqlTokenType.QuotedIdentifier or
            TSqlTokenType.RightParenthesis or
            TSqlTokenType.Variable or
            TSqlTokenType.AsciiStringLiteral or
            TSqlTokenType.UnicodeStringLiteral or
            TSqlTokenType.Integer or
            TSqlTokenType.Numeric or
            TSqlTokenType.Real or
            TSqlTokenType.Money or
            TSqlTokenType.HexLiteral => true,
            _ => false,
        };
    }
}
