using AkmlSql.PostgreSql.Reference;

namespace AkmlSql.PostgreSql.Formatting;

internal sealed partial class PgLayout
{
    // ═══ parentheses ═════════════════════════════════════════════════════════════════════════════════

    /// <summary>One set of parenthesis options: the global ones, or the DDL / CTE / INSERT overrides, which carry the same settings.</summary>
    private readonly record struct ParenSpec(string Style, bool IndentContents, bool SpacesInside);

    private ParenSpec GlobalParens() => new(S.Parentheses.Style, S.Parentheses.IndentContents, S.Parentheses.SpacesInside);

    private ParenSpec DdlParens() => new(S.Ddl.ParenthesisStyle, S.Ddl.IndentParenthesesContents, S.Parentheses.SpacesInside);

    /// <summary>
    /// "( content )". Content that fits on the line stays inline; otherwise the parenthesis style decides where the brackets go:
    /// compact keeps the content right after "(", expanded moves it to the next line (split also puts "(" on a line of its own);
    /// ")" hugs the content (simple), goes to the line start (to statement), one tab in (indented) or under "(" (right-aligned).
    /// Indent contents indents expanded content one tab and compact continuation lines one tab from "(".
    /// </summary>
    private PgDoc Parens(int open, int close, ParenSpec spec, PgDoc content, bool suppressTrailing = false, bool force = false)
    {
        var o = Emit(open);
        var c = Emit(close, !suppressTrailing);
        var sp = spec.SpacesInside ? PgDoc.Text(" ") : PgDoc.Empty;
        var flat = PgDoc.Concat(o, sp, content, sp, c);

        var style = string.IsNullOrEmpty(spec.Style) ? "compactSimple" : spec.Style;
        var expanded = style.StartsWith("expanded", StringComparison.Ordinal);
        var baseAnchor = style is "expandedRightAligned" or "compactRightAligned" ? PgAnchor.Column : PgAnchor.LineStart;
        var contentIndent = spec.IndentContents ? Tab : 0;

        var parts = new List<PgDoc>();
        if (style == "expandedSplit") parts.Add(PgDoc.ScopedLine(PgAnchor.LineStart, 0, string.Empty));
        parts.Add(o);
        if (expanded)
        {
            parts.Add(PgDoc.ScopedLine(baseAnchor, contentIndent, string.Empty));
            parts.Add(PgDoc.ScopedIndent(baseAnchor, contentIndent, content));
        }
        else
        {
            parts.Add(sp);
            parts.Add(spec.IndentContents ? PgDoc.ScopedIndent(PgAnchor.Column, Tab, content) : PgDoc.Align(content));
        }

        switch (style)
        {
            case "compactToStatement":
            case "expandedToStatement":
            case "expandedSplit":
                parts.Add(PgDoc.ScopedLine(PgAnchor.LineStart, 0, string.Empty));
                break;
            case "compactIndented":
            case "expandedIndented":
                parts.Add(PgDoc.ScopedLine(PgAnchor.LineStart, Tab, string.Empty));
                break;
            case "compactRightAligned":
            case "expandedRightAligned":
                parts.Add(PgDoc.ScopedLine(PgAnchor.Column, 0, string.Empty));
                break;
            default:
                parts.Add(sp);
                break;
        }
        parts.Add(c);

        var broken = PgDoc.Scope(PgDoc.Concat(parts));
        var choice = PgDoc.IfBreak(broken, flat);
        return PgDoc.Group(force ? PgDoc.Concat(PgDoc.BreakParent, choice) : choice);
    }

    // ═══ what a parenthesis holds ════════════════════════════════════════════════════════════════════

    private bool IsQueryStart(int i)
    {
        if (i >= T.Count) return false;
        var t = T[i];
        if (t.Kind == PgFt.Open) return IsQueryStart(i + 1);
        return t.IsAny("SELECT", "WITH", "VALUES", "TABLE");
    }

    private PgDoc ParenElement(int open, int close, int flowLo, bool suppressTrailing)
    {
        if (open + 1 < close && IsQueryStart(open + 1)) return Subquery(open, close, suppressTrailing);
        if (IsCallParen(open, flowLo)) return CallArguments(open, close, suppressTrailing);
        if (open > flowLo && T[open - 1].Is("IN")) return InList(open, close, suppressTrailing);
        return GroupedExpression(open, close, suppressTrailing);
    }

    private bool IsCallParen(int open, int flowLo)
    {
        if (open <= flowLo) return false;
        var prev = T[open - 1];
        if (prev.Kind == PgFt.Quoted) return true;
        if (prev.Kind != PgFt.Word) return false;
        if (PgWords.SpaceBeforeParen.Contains(prev.Upper)) return false;
        return IsKnownCallable(open - 1) || IsKnownType(open - 1) || Adjacent(open - 1, open) || prev.Is("ARRAY");
    }

    private PgDoc Subquery(int open, int close, bool suppressTrailing)
    {
        var content = Query(open + 1, close, QueryContext.Subquery);
        return Parens(open, close, GlobalParens(), content, suppressTrailing);
    }

    /// <summary>A parenthesised expression or condition: <c>(a + b) * c</c>, <c>(x = 1 OR y = 2)</c>.</summary>
    private PgDoc GroupedExpression(int open, int close, bool suppressTrailing)
    {
        var content = Condition(open + 1, close, new ConditionContext(0, 0));
        return Parens(open, close, GlobalParens(), content, suppressTrailing);
    }

    // ═══ function calls ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// "( arguments )" of a call: spaces around the parentheses and the argument list, a space between empty parentheses, and the
    /// arguments one to a line (aligned with the first) when the style asks for it or the call is longer than the line.
    /// </summary>
    private PgDoc CallArguments(int open, int close, bool suppressTrailing)
    {
        var fc = S.FunctionCalls;
        var o = Emit(open);
        var c = Emit(close, !suppressTrailing);
        if (open + 1 == close)
            return PgDoc.Concat(o, fc.SpaceBetweenEmptyParentheses ? PgDoc.Text(" ") : PgDoc.Empty, c);

        var items = SplitCommas(open + 1, close);
        var docs = new List<PgDoc>();
        var commas = new List<int>();
        var previousLast = new List<int>();
        foreach (var (a, b, comma) in items)
        {
            docs.Add(Flow(a, b, hoistLastTrailing: comma >= 0 && b > a && T[b - 1].TrailingLine is not null));
            commas.Add(comma);
            previousLast.Add(b > a ? b - 1 : -1);
        }

        var list = ItemList(docs, commas, previousLast, new ListSpec
        {
            FirstOnNewLine = "never",
            Subsequent = fc.PlaceArgumentsOnNewLines,
            AlignToFirst = true,
            Indent = false,
        });

        var inside = fc.SpacesAroundArgumentList ? PgDoc.Text(" ") : PgDoc.Empty;
        return PgDoc.Concat(o, inside, list, inside, c);
    }

    // ═══ IN (...) ════════════════════════════════════════════════════════════════════════════════════

    private PgDoc InList(int open, int close, bool suppressTrailing)
    {
        var o = S.Operators;
        var opening = Emit(open);
        var closing = Emit(close, !suppressTrailing);
        var inside = o.InSpaceAroundContents ? PgDoc.Text(" ") : PgDoc.Empty;
        if (open + 1 == close) return PgDoc.Concat(opening, closing);

        var items = SplitCommas(open + 1, close);
        var docs = new List<PgDoc>();
        var commas = new List<int>();
        var previousLast = new List<int>();
        foreach (var (a, b, comma) in items)
        {
            docs.Add(Flow(a, b, hoistLastTrailing: comma >= 0 && b > a && T[b - 1].TrailingLine is not null));
            commas.Add(comma);
            previousLast.Add(b > a ? b - 1 : -1);
        }

        var multi = docs.Count > 1;
        var firstNew = o.InFirstValueOnNewLine switch
        {
            "always" => true,
            "ifSubsequentValues" => multi,
            _ => false,
        };
        var firstIfLonger = o.InFirstValueOnNewLine == "ifLongerThanMaxLineLength";

        var values = ItemList(docs, commas, previousLast, new ListSpec
        {
            FirstOnNewLine = "never",
            Subsequent = o.InSubsequentValuesOnNewLines,
            AlignToFirst = true,
            Indent = false,
        });

        PgDoc body;
        if (firstNew || firstIfLonger)
        {
            // First value on its own line: always / when there are several / when the list does not fit on the line.
            var newLine = PgDoc.ScopedIndent(PgAnchor.Column, 1 + (o.InSpaceAroundContents ? 1 : 0),
                PgDoc.Concat(PgDoc.Line(string.Empty), values));
            body = firstNew
                ? PgDoc.Block(PgDoc.Concat(opening, inside, Nested(newLine), inside, closing))
                : PgDoc.Group(PgDoc.Concat(opening, inside, PgDoc.IfBreak(Nested(newLine), values), inside, closing));
        }
        else
        {
            body = PgDoc.Concat(opening, inside, values, inside, closing);
        }
        return PgDoc.Scope(body);
    }

    private static PgDoc Nested(PgDoc doc) => doc;

    // ═══ lists ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>How one comma-separated list is laid out: the Lists options plus the per-construct overrides.</summary>
    private sealed class ListSpec
    {
        /// <summary>always | never | ifSubsequentItems | ifMultiple | ifMultipleItems | ifSubsequentValues</summary>
        public string FirstOnNewLine { get; init; } = "never";

        /// <summary>always | never | ifLongerThanMaxLineLength</summary>
        public string Subsequent { get; init; } = "always";

        public bool AlignToFirst { get; init; } = true;
        public bool Indent { get; init; } = true;

        /// <summary>Columns from the statement's column to the items' column, for commas aligned "to statement".</summary>
        public int ItemColumnFromStatement { get; init; }
    }

    /// <summary>
    /// A comma-separated list. The first item goes on the keyword's line or the next, subsequent items on their own lines (always),
    /// only when the list is too long, or never; they align with the first item or indent from the keyword; commas follow the items
    /// or lead them. The caller has put the list's start at the column of its first item (on the keyword's line), inside an
    /// <c>Align</c> at the owning keyword's column when the first item is to go on a line of its own.
    /// </summary>
    private PgDoc ItemList(IReadOnlyList<PgDoc> items, IReadOnlyList<int> commas, IReadOnlyList<int> previousLast, ListSpec spec, PgDoc? pad = null)
    {
        var n = items.Count;
        if (n == 0) return PgDoc.Empty;

        var afterComma = S.Lists.SpaceAfterComma ? 1 : 0;
        var flatSeparator = (int index) => PgDoc.Concat(CommaDoc(commas[index], previousLast[index]), PgDoc.Text(afterComma == 1 ? " " : string.Empty));

        var firstNew = spec.FirstOnNewLine switch
        {
            "always" => true,
            "ifSubsequentItems" or "ifMultiple" or "ifMultipleItems" or "ifSubsequentValues" => n > 1,
            _ => false,
        };

        PgDoc joined;
        if (n == 1)
        {
            joined = items[0];
        }
        else if (spec.Subsequent == "never")
        {
            var parts = new List<PgDoc> { items[0] };
            for (var i = 1; i < n; i++)
            {
                parts.Add(flatSeparator(i - 1));
                parts.Add(items[i]);
            }
            joined = PgDoc.Concat(parts);
        }
        else
        {
            var commaOffset = S.Lists.CommaAlignment switch
            {
                "beforeItem" => -(1 + afterComma),
                "toStatement" => -spec.ItemColumnFromStatement,
                _ => 0,
            };
            var parts = new List<PgDoc> { items[0] };
            for (var i = 1; i < n; i++)
            {
                PgDoc separator, item;
                if (S.Lists.CommasBeforeItems)
                {
                    // Broken: the comma starts the line, and the comment that ended the previous item's line stays on that line;
                    // flat: the comma follows the previous item as usual.
                    parts[^1] = PgDoc.Concat(parts[^1], CommaInlineComments(commas[i - 1]), CommaSuffix(commas[i - 1], previousLast[i - 1]));
                    var broken = PgDoc.Concat(
                        PgDoc.Nest(commaOffset, PgDoc.Line(string.Empty)),
                        CommaText(commas[i - 1], inlineCommentsElsewhere: true),
                        PgDoc.Text(afterComma == 1 ? " " : string.Empty));
                    var flatComma = PgDoc.Concat(CommaText(commas[i - 1], inlineCommentsElsewhere: true), PgDoc.Text(afterComma == 1 ? " " : string.Empty));
                    separator = PgDoc.IfBreak(broken, flatComma);
                    item = PgDoc.Align(items[i]);
                }
                else
                {
                    separator = PgDoc.Concat(CommaDoc(commas[i - 1], previousLast[i - 1]), PgDoc.Line(afterComma == 1 ? " " : string.Empty));
                    item = items[i];
                }
                parts.Add(separator);
                parts.Add(item);
            }
            var all = PgDoc.Concat(parts);
            joined = spec.Subsequent == "always" ? PgDoc.Block(all) : PgDoc.Group(all);
        }

        var indentOffset = spec.Indent ? Tab : 0;
        if (firstNew) return PgDoc.Nest(indentOffset, PgDoc.Concat(PgDoc.Line(" "), spec.AlignToFirst ? PgDoc.Align(joined) : joined));
        var onKeywordLine = spec.AlignToFirst ? PgDoc.Align(joined) : PgDoc.Nest(indentOffset, joined);
        return pad is null ? onKeywordLine : PgDoc.Concat(pad, onKeywordLine);
    }

    // ═══ CASE ════════════════════════════════════════════════════════════════════════════════════════

    private PgDoc CaseElement(int caseIndex, int endIndex, bool suppressTrailing)
    {
        var c = S.Case;
        var parts = new List<PgDoc>();
        parts.Add(Kw(caseIndex));

        // CASE [input] WHEN ... THEN ... [ELSE ...] END
        var whens = FindAllTop(caseIndex + 1, endIndex, t => t.Kind == PgFt.Word && t.Upper == "WHEN");
        if (whens.Count == 0) return Flow(caseIndex, endIndex + 1);

        var hasInput = whens[0] > caseIndex + 1;
        if (hasInput)
        {
            parts.Add(PgDoc.Text(" "));
            parts.Add(ConditionOrFlow(caseIndex + 1, whens[0]));
        }

        var elseIndex = FindTop(whens[^1] + 1, endIndex, t => t.Kind == PgFt.Word && t.Upper == "ELSE");
        var lastBranchEnd = elseIndex < endIndex ? elseIndex : endIndex;

        var firstWhenNew = c.PlaceFirstWhenOnNewLine switch
        {
            "never" => false,
            "ifInputExpression" => hasInput,
            _ => true,
        };
        var whenOffset = c.WhenAlignment switch
        {
            "toCase" => 0,
            "toFirstItem" => T[caseIndex].Text.Length + 1,
            _ => Tab,
        };
        var thenOffset = c.ThenAlignment switch
        {
            "toWhen" => 0,
            "toWhenExpression" => T[whens[0]].Text.Length + 1,
            _ => Tab,
        };

        var collapse = c.CollapseShort ? c.CollapseShorterThan - 1 : (int?)null;

        for (var i = 0; i < whens.Count; i++)
        {
            var when = whens[i];
            var branchEnd = i + 1 < whens.Count ? whens[i + 1] : lastBranchEnd;
            var thenIndex = FindTop(when + 1, branchEnd, t => t.Kind == PgFt.Word && t.Upper == "THEN");

            var condition = Condition(when + 1, thenIndex < branchEnd ? thenIndex : branchEnd,
                new ConditionContext(T[when].Text.Length, T[when].Text.Length + 1));
            var branch = new List<PgDoc> { Kw(when), PgDoc.Text(" "), PgDoc.Align(condition) };

            if (thenIndex < branchEnd)
            {
                var result = ConditionOrFlow(thenIndex + 1, branchEnd);
                if (c.PlaceThenOnNewLine)
                {
                    branch.Add(PgDoc.Nest(thenOffset, PgDoc.Concat(PgDoc.Line(" "), Kw(thenIndex))));
                    branch.Add(c.PlaceExpressionOnNewLine
                        ? PgDoc.Nest(thenOffset + Tab, PgDoc.Concat(PgDoc.Line(" "), PgDoc.Align(result)))
                        : PgDoc.Concat(PgDoc.Text(" "), PgDoc.Align(result)));
                }
                else
                {
                    branch.Add(PgDoc.Text(" "));
                    branch.Add(Kw(thenIndex));
                    branch.Add(c.PlaceExpressionOnNewLine
                        ? PgDoc.Nest(Tab, PgDoc.Concat(PgDoc.Line(" "), PgDoc.Align(result)))
                        : PgDoc.Concat(PgDoc.Text(" "), PgDoc.Align(result)));
                }
            }

            var lead = i == 0 && !firstWhenNew ? (PgDoc)PgDoc.Text(" ") : PgDoc.Line(" ");
            parts.Add(PgDoc.Nest(whenOffset, PgDoc.Concat(lead, PgDoc.Align(PgDoc.Concat(branch)))));
        }

        if (elseIndex < endIndex)
        {
            var result = ConditionOrFlow(elseIndex + 1, endIndex);
            if (c.PlaceElseOnNewLine)
            {
                var elseOffset = c.AlignElseToWhen ? whenOffset : (c.PlaceThenOnNewLine ? whenOffset + thenOffset : whenOffset + Tab);
                parts.Add(PgDoc.Nest(elseOffset, PgDoc.Concat(
                    PgDoc.Line(" "),
                    PgDoc.Align(PgDoc.Concat(
                        Kw(elseIndex),
                        c.PlaceExpressionOnNewLine
                            ? PgDoc.Nest(Tab, PgDoc.Concat(PgDoc.Line(" "), PgDoc.Align(result)))
                            : PgDoc.Concat(PgDoc.Text(" "), PgDoc.Align(result)))))));
            }
            else
            {
                parts.Add(PgDoc.Text(" "));
                parts.Add(PgDoc.Align(PgDoc.Concat(
                    Kw(elseIndex),
                    c.PlaceExpressionOnNewLine
                        ? PgDoc.Nest(Tab, PgDoc.Concat(PgDoc.Line(" "), PgDoc.Align(result)))
                        : PgDoc.Concat(PgDoc.Text(" "), PgDoc.Align(result)))));
            }
        }

        if (c.PlaceEndOnNewLine)
        {
            var endOffset = c.EndAlignment switch
            {
                "toWhen" => whenOffset,
                "rightAlignedToWhen" => whenOffset + 1,
                _ => 0,
            };
            parts.Add(PgDoc.Nest(endOffset, PgDoc.Concat(PgDoc.Line(" "), Kw(endIndex, !suppressTrailing))));
        }
        else
        {
            parts.Add(PgDoc.Text(" "));
            parts.Add(Kw(endIndex, !suppressTrailing));
        }

        return PgDoc.Align(PgDoc.Block(PgDoc.Concat(parts), collapse));
    }

    // ═══ conditions: AND / OR chains ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Where a condition list sits relative to the keyword that owns it (WHERE, ON, WHEN, a parenthesis): the keyword's width and how far
    /// right of the keyword the first condition starts. AND / OR align against these.
    /// </summary>
    private readonly record struct ConditionContext(int KeywordWidth, int FirstOffset);

    private PgDoc ConditionOrFlow(int lo, int hi) => Condition(lo, hi, new ConditionContext(0, 0));

    /// <summary>The AND / OR operators that join conditions in [lo, hi): top-level, and not the AND of a BETWEEN.</summary>
    private List<int> ConditionOperators(int lo, int hi)
    {
        var operators = new List<int>();
        var between = 0;
        var i = lo;
        while (i < hi)
        {
            i = FindTop(i, hi, t => t.Kind == PgFt.Word && (t.Upper is "AND" or "OR" or "BETWEEN"));
            if (i >= hi) break;
            var t = T[i];
            if (t.Upper == "BETWEEN") between++;
            else if (t.Upper == "AND" && between > 0) between--;
            else operators.Add(i);
            i++;
        }
        return operators;
    }

    /// <summary>A condition (or any expression) as a chain of AND / OR operands laid out per the operator options.</summary>
    private PgDoc Condition(int lo, int hi, ConditionContext context)
    {
        if (lo >= hi) return PgDoc.Empty;
        var operators = ConditionOperators(lo, hi);
        if (operators.Count == 0) return ConditionOperand(lo, hi);

        var o = S.Operators;
        var operands = new List<(int Lo, int Hi)>();
        var start = lo;
        foreach (var op in operators)
        {
            operands.Add((start, op));
            start = op + 1;
        }
        operands.Add((start, hi));

        int OperatorOffset(int width) => o.AndOrAlignment switch
        {
            "rightAligned" => context.KeywordWidth - width - context.FirstOffset,
            "beforeFirstListItem" => -(width + 1),
            "toFirstListItem" => 0,
            "indented" => Tab - context.FirstOffset,
            _ => -context.FirstOffset,
        };
        var conditionOffset = o.AndOrAlignment switch
        {
            "leftAligned" => -context.FirstOffset,
            "indented" => Tab - context.FirstOffset,
            _ => 0,
        };

        var parts = new List<PgDoc> { ConditionOperand(operands[0].Lo, operands[0].Hi) };
        for (var i = 0; i < operators.Count; i++)
        {
            var op = operators[i];
            var operand = PgDoc.Align(ConditionOperand(operands[i + 1].Lo, operands[i + 1].Hi));
            if (o.AndOrBeforeCondition)
            {
                parts.Add(PgDoc.Nest(OperatorOffset(T[op].Text.Length), PgDoc.Line(" ")));
                parts.Add(Kw(op));
                parts.Add(PgDoc.Text(" "));
                parts.Add(operand);
            }
            else
            {
                parts.Add(PgDoc.Text(" "));
                parts.Add(Kw(op));
                parts.Add(PgDoc.Nest(conditionOffset, PgDoc.Line(" ")));
                parts.Add(operand);
            }
        }

        var chain = PgDoc.Concat(parts);
        return o.AndOrPlaceOnNewLine switch
        {
            "never" => PgDoc.Concat(ChainFlat(operands, operators)),
            "always" => PgDoc.Align(PgDoc.Block(chain)),
            _ => PgDoc.Align(PgDoc.Group(chain)),
        };
    }

    private PgDoc ChainFlat(List<(int Lo, int Hi)> operands, List<int> operators)
    {
        var parts = new List<PgDoc> { ConditionOperand(operands[0].Lo, operands[0].Hi) };
        for (var i = 0; i < operators.Count; i++)
        {
            parts.Add(PgDoc.Text(" "));
            parts.Add(Kw(operators[i]));
            parts.Add(PgDoc.Text(" "));
            parts.Add(ConditionOperand(operands[i + 1].Lo, operands[i + 1].Hi));
        }
        return PgDoc.Concat(parts);
    }

    /// <summary>One operand of a chain: a BETWEEN with its own line options, or a plain run of tokens.</summary>
    private PgDoc ConditionOperand(int lo, int hi)
    {
        var between = FindTop(lo, hi, t => t.Kind == PgFt.Word && t.Upper == "BETWEEN");
        if (between >= hi) return Flow(lo, hi);

        var andIndex = FindTop(between + 1, hi, t => t.Kind == PgFt.Word && t.Upper == "AND");
        if (andIndex >= hi) return Flow(lo, hi);

        var o = S.Operators;
        var keywordStart = between > lo && T[between - 1].Is("NOT") ? between - 1 : between;
        var left = Flow(lo, keywordStart);
        var keyword = Phrase(keywordStart, between);
        var low = Flow(between + 1, andIndex);
        var high = Flow(andIndex + 1, hi);
        var and = Kw(andIndex);

        var keywordWidth = PhraseWidth(keywordStart, between);
        var andOffset = o.BetweenAndAlignment switch
        {
            "rightAlignedToBetween" => keywordWidth - T[andIndex].Text.Length,
            "toBeginningOfExpression" => -Tab * (o.BetweenOnNewLine ? 1 : 0),
            _ => 0,
        };

        var parts = new List<PgDoc> { left };
        parts.Add(o.BetweenOnNewLine ? PgDoc.Nest(Tab, PgDoc.Line(" ")) : PgDoc.Text(" "));
        var tail = new List<PgDoc> { keyword, PgDoc.Text(" "), low };
        tail.Add(o.BetweenAndOnNewLine ? PgDoc.Nest(andOffset, PgDoc.Line(" ")) : PgDoc.Text(" "));
        tail.Add(and);
        tail.Add(PgDoc.Text(" "));
        tail.Add(high);
        parts.Add(PgDoc.Align(PgDoc.Concat(tail)));
        return PgDoc.Block(PgDoc.Concat(parts));
    }
}
