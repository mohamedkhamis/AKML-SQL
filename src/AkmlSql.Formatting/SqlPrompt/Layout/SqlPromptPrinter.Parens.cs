using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

internal sealed partial class SqlPromptPrinter
{
    /// <summary>
    /// One set of parenthesis options: the global ones, or the DDL / CTE / INSERT columns / INSERT
    /// values overrides, which carry the same four settings.
    /// </summary>
    private readonly record struct ParenSpec(string Style, bool IndentContents, bool SpacesInside, int CollapseBelow);

    private ParenSpec GlobalParens(bool subquery = false)
    {
        var p = S.Parentheses;
        var collapse = p.CollapseShort ? p.CollapseShorterThan : 0;
        if (subquery && S.Dml.CollapseShortSubqueries)
            collapse = Math.Max(collapse, S.Dml.CollapseSubqueriesShorterThan);
        return new ParenSpec(p.Style, p.IndentContents, p.SpacesInside, collapse);
    }

    private ParenSpec DdlParens() =>
        new(S.Ddl.ParenthesisStyle, S.Ddl.IndentParenthesesContents, S.Parentheses.SpacesInside,
            S.Parentheses.CollapseShort ? S.Parentheses.CollapseShorterThan : 0);

    /// <summary>
    /// Prints "( content )". Content that fits on the line — or collapses under the style's
    /// threshold — stays inline; otherwise the parenthesis style decides where the brackets go:
    /// <list type="bullet">
    /// <item><b>compact</b>: content starts right after "(";
    /// <b>expanded</b>: "(" ends its line and the content starts on the next one
    /// (<b>split</b>: "(" on a line of its own).</item>
    /// <item>")" hugs the content (<b>simple</b>), or goes on its own line aligned to the
    /// statement (<b>to statement</b>, <b>split</b>), indented one tab from it
    /// (<b>indented</b>), or under the "(" (<b>right-aligned</b>).</item>
    /// <item><b>Indent contents</b>: expanded content is indented one tab; compact content's
    /// continuation lines are indented one tab from the "(" instead of aligned just after it.</item>
    /// </list>
    /// The caller has set the whitespace before "(". <paramref name="content"/> receives the
    /// column its continuation lines should use.
    /// </summary>
    private void Parens(int open, int close, ParenSpec spec, Action<int> content)
    {
        var forced = _forceParenBreak;
        _forceParenBreak = false;
        W.Until(open);
        var lineStart = W.LineStart;
        var parenColumn = W.Column;

        void Inline()
        {
            W.Through(open);
            W.SpaceIf(spec.SpacesInside);
            content(W.Column);
            W.Until(close);
            W.SpaceIf(spec.SpacesInside);
            W.Through(close);
        }

        if (W.IsFlat)
        {
            Inline();
            return;
        }

        // Collapse short contents onto one line.
        if (spec.CollapseBelow > 0 && !forced)
        {
            var width = W.MeasureFlat(() => { content(W.Column); W.Until(close); }, open + 1);
            if (width < spec.CollapseBelow && parenColumn + width + 2 <= Max)
            {
                W.Flat(Inline);
                return;
            }
        }

        // Content that fits on the line needs no layout at all.
        if (!forced && W.TryOneLine(Inline, Max - TrailingWidth(close))) return;

        var style = spec.Style;
        var expanded = style.StartsWith("expanded", StringComparison.Ordinal);
        var baseColumn = style is "expandedRightAligned" or "compactRightAligned" ? parenColumn : lineStart;

        if (style == "expandedSplit")
        {
            W.NewLine(lineStart);
            parenColumn = lineStart;
            baseColumn = lineStart;
        }

        W.Through(open);
        int contentColumn;
        if (expanded)
        {
            contentColumn = baseColumn + (spec.IndentContents ? Tab : 0);
            W.NewLine(contentColumn);
        }
        else
        {
            W.SpaceIf(spec.SpacesInside);
            contentColumn = spec.IndentContents ? parenColumn + Tab : W.Column;
        }

        content(contentColumn);
        W.Until(close);

        switch (style)
        {
            case "compactToStatement" or "expandedToStatement" or "expandedSplit":
                W.NewLine(lineStart);
                break;
            case "compactIndented" or "expandedIndented":
                W.NewLine(lineStart + Tab);
                break;
            case "compactRightAligned" or "expandedRightAligned":
                W.NewLine(parenColumn);
                break;
            default: // simple: ")" follows the content
                W.SpaceIf(spec.SpacesInside);
                break;
        }
        W.Through(close);
    }

    /// <summary>
    /// Width of what stays on the line after token <paramref name="after"/>: the rest of the
    /// current list item or condition (") AS Label", " = 1"), up to the next comma, AND / OR,
    /// clause keyword, or closing bracket of an enclosing group — where the layout may break.
    /// A fit decision that ignored it would leave that tail hanging past the wrap width.
    /// </summary>
    private int TrailingWidth(int after)
    {
        var width = 0;
        var depth = 0;
        for (var i = after + 1; i < T.Count; i++)
        {
            var t = T[i];
            switch (t.TokenType)
            {
                case TSqlTokenType.WhiteSpace or TSqlTokenType.MultilineComment:
                    continue;
                case TSqlTokenType.SingleLineComment or TSqlTokenType.EndOfFile or TSqlTokenType.Semicolon:
                    return width;
                case TSqlTokenType.LeftParenthesis:
                    depth++;
                    break;
                case TSqlTokenType.RightParenthesis:
                    if (depth == 0) return width + 1;
                    depth--;
                    break;
                case TSqlTokenType.Comma when depth == 0:
                    return width + 1;
                case TSqlTokenType.And or TSqlTokenType.Or or TSqlTokenType.From or TSqlTokenType.Where or TSqlTokenType.Group
                    or TSqlTokenType.Having or TSqlTokenType.Order or TSqlTokenType.Union or TSqlTokenType.Except
                    or TSqlTokenType.Intersect or TSqlTokenType.Into or TSqlTokenType.When or TSqlTokenType.Then
                    or TSqlTokenType.Else or TSqlTokenType.End or TSqlTokenType.On or TSqlTokenType.Inner
                    or TSqlTokenType.Left or TSqlTokenType.Right or TSqlTokenType.Full or TSqlTokenType.Cross
                    or TSqlTokenType.Join or TSqlTokenType.Outer or TSqlTokenType.Select or TSqlTokenType.Values
                    or TSqlTokenType.Set or TSqlTokenType.Begin or TSqlTokenType.Go
                    when depth == 0:
                    return width;
            }
            width += (width == 0 ? 0 : 1) + t.Text.Length;
            if (width > Max) return width;
        }
        return width;
    }

    /// <summary>Spacing before a "(" that is not a function call's (IN (…), VALUES (…), TOP (…)…).</summary>
    private void BeforeParen() => W.SpaceIf(S.Parentheses.SpacesAround);

    /// <summary>A parenthesized subquery: global parenthesis options plus "collapse short subqueries".</summary>
    private void Subquery(int open, int close, QueryExpression query)
    {
        Parens(open, close, GlobalParens(subquery: true), column => Query(query, column));
    }

    /// <summary>The "(" and ")" around a fragment wrapped in parentheses, or (-1, -1).</summary>
    private (int Open, int Close) OuterParens(TSqlFragment f)
    {
        var open = Find(f.FirstTokenIndex, f.LastTokenIndex, TSqlTokenType.LeftParenthesis);
        if (open != f.FirstTokenIndex) return (-1, -1);
        var close = MatchingParen(open);
        return close == f.LastTokenIndex ? (open, close) : (-1, -1);
    }
}
