using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

internal sealed partial class SqlPromptPrinter
{
    // ── scalar expressions ───────────────────────────────────────────────────

    private void Scalar(ScalarExpression? e)
    {
        if (e is null) return;
        switch (e)
        {
            case BinaryExpression b:
                Binary(b);
                break;
            case UnaryExpression u:
                W.Until(u.Expression.FirstTokenIndex);  // the sign
                W.NoSpace();
                Scalar(u.Expression);
                break;
            case ParenthesisExpression p:
                Parens(p.FirstTokenIndex, p.LastTokenIndex, GlobalParens(), _ => Scalar(p.Expression));
                break;
            case ScalarSubquery sq:
                Subquery(sq.FirstTokenIndex, sq.LastTokenIndex, sq.QueryExpression);
                break;
            case SearchedCaseExpression or SimpleCaseExpression:
                Case(e);
                break;
            case FunctionCall f:
                FunctionCallExpr(f);
                break;
            case CoalesceExpression c:
                Call(e, c.Expressions.Cast<TSqlFragment>().ToList());
                break;
            case NullIfExpression n:
                Call(e, [n.FirstExpression, n.SecondExpression]);
                break;
            case IIfCall iif:
                Call(e, [iif.Predicate, iif.ThenExpression, iif.ElseExpression]);
                break;
            case LeftFunctionCall l:
                Call(e, l.Parameters.Cast<TSqlFragment>().ToList());
                break;
            case RightFunctionCall r:
                Call(e, r.Parameters.Cast<TSqlFragment>().ToList());
                break;
            case CastCall or ConvertCall or TryCastCall or TryConvertCall or ParseCall or TryParseCall:
                Call(e, null);
                break;
            default:
                W.Through(e.LastTokenIndex);
                break;
        }
        W.Through(e.LastTokenIndex);
    }

    /// <summary>
    /// Arithmetic, string concatenation and bitwise operators: spaced per "add spaces around
    /// arithmetic operators"; when the whole expression does not fit, it wraps before an operator
    /// (lined up with the start of the expression).
    /// </summary>
    private void Binary(BinaryExpression b)
    {
        var operands = new List<ScalarExpression>();
        var operators = new List<int>();
        void Flatten(ScalarExpression x)
        {
            if (x is BinaryExpression bb)
            {
                Flatten(bb.FirstExpression);
                operators.Add(NextSignificant(bb.FirstExpression.LastTokenIndex + 1));
                Flatten(bb.SecondExpression);
            }
            else
            {
                operands.Add(x);
            }
        }
        Flatten(b);

        var spaced = S.Operators.SpacesAroundArithmetic;
        void Inline()
        {
            Scalar(operands[0]);
            for (var i = 0; i < operators.Count; i++)
            {
                W.Until(operators[i]);
                W.SpaceIf(spaced);
                W.Through(operators[i]);
                W.SpaceIf(spaced);
                Scalar(operands[i + 1]);
            }
        }

        if (W.IsFlat || !S.Whitespace.WrapLongLines)
        {
            Inline();
            return;
        }
        if (W.TryOneLine(Inline)) return;

        var start = W.Column;
        Scalar(operands[0]);
        for (var i = 0; i < operators.Count; i++)
        {
            W.Until(operators[i]);
            var next = operands[i + 1];
            var width = W.MeasureFlat(() => Scalar(next), next.FirstTokenIndex) + T[operators[i]].Text.Length + 2;
            if (W.Column + width > Max) W.NewLine(start);
            else W.SpaceIf(spaced);
            W.Through(operators[i]);
            W.SpaceIf(spaced);
            Scalar(next);
        }
    }

    // ── function calls ───────────────────────────────────────────────────────

    private void FunctionCallExpr(FunctionCall f)
    {
        var open = Find(f.FunctionName.LastTokenIndex + 1, f.LastTokenIndex, TSqlTokenType.LeftParenthesis);
        if (open < 0)
        {
            W.Through(f.LastTokenIndex);
            return;
        }
        var close = MatchingParen(open);
        Arguments(open, close, f.Parameters.Cast<TSqlFragment>().ToList());

        // WITHIN GROUP (…) / OVER (…) / FILTER: as written, default spacing.
        if (close < f.LastTokenIndex)
        {
            W.Space();
            W.Through(f.LastTokenIndex);
        }
    }

    /// <summary>A function-shaped call (COALESCE, NULLIF, IIF, LEFT, CAST, CONVERT…).</summary>
    private void Call(ScalarExpression e, IList<TSqlFragment>? arguments)
    {
        var open = Find(e.FirstTokenIndex, e.LastTokenIndex, TSqlTokenType.LeftParenthesis);
        if (open < 0)
        {
            W.Through(e.LastTokenIndex);
            return;
        }
        var close = MatchingParen(open);
        if (arguments is null)
        {
            // CAST(x AS type), CONVERT(type, x, style): the call's own spacing, the inside as written.
            W.Until(open);
            W.SpaceIf(S.FunctionCalls.SpacesAroundParentheses);
            W.Through(open);
            W.SpaceIf(S.FunctionCalls.SpacesAroundArgumentList);
            var inner = e switch
            {
                CastCall c => c.Parameter,
                TryCastCall c => c.Parameter,
                _ => null,
            };
            if (inner is not null) Scalar(inner);
            W.Until(close);
            W.SpaceIf(S.FunctionCalls.SpacesAroundArgumentList);
            W.Through(close);
            return;
        }
        Arguments(open, close, arguments);
    }

    /// <summary>
    /// "( arguments )" of a call: the Function calls options — spaces around the parentheses,
    /// around the argument list, between empty parentheses, and when arguments go on new lines
    /// (aligned with the first argument).
    /// </summary>
    private void Arguments(int open, int close, IList<TSqlFragment> args)
    {
        var fc = S.FunctionCalls;
        W.Until(open);
        W.SpaceIf(fc.SpacesAroundParentheses);
        W.Through(open);

        if (args.Count == 0)
        {
            W.Until(close);
            W.SpaceIf(fc.SpaceBetweenEmptyParentheses);
            W.Through(close);
            return;
        }

        W.SpaceIf(fc.SpacesAroundArgumentList);
        if (NextSignificant(open + 1) < args[0].FirstTokenIndex)
        {
            // COUNT(DISTINCT x), STRING_AGG(ALL …)
            W.Until(args[0].FirstTokenIndex);
            W.Space();
        }

        var start = W.Column;
        List(args, new ListLayout
        {
            KeywordColumn = start,
            StatementColumn = start,
            FirstOnNewLine = "never",
            Subsequent = fc.PlaceArgumentsOnNewLines,
            AlignToFirst = true,
            Indent = false,
            TrailingWidth = 1 + TrailingWidth(close),
        }, (a, _) => Fragment(a));

        W.Until(close);
        W.SpaceIf(fc.SpacesAroundArgumentList);
        W.Through(close);
    }

    private void Fragment(TSqlFragment f)
    {
        switch (f)
        {
            case ScalarExpression s: Scalar(s); break;
            case BooleanExpression b: Boolean(b, null); break;
            default: W.Through(f.LastTokenIndex); break;
        }
        W.Through(f.LastTokenIndex);
    }

    // ── CASE ─────────────────────────────────────────────────────────────────

    private void Case(ScalarExpression e)
    {
        if (S.Case.CollapseShort && !W.IsFlat)
        {
            var width = W.MeasureFlat(() => CaseBody(e), e.FirstTokenIndex);
            if (width < S.Case.CollapseShorterThan && W.Column + width <= Max)
            {
                W.Flat(() => CaseBody(e));
                return;
            }
        }
        CaseBody(e);
    }

    /// <summary>
    /// CASE layout: first WHEN on the CASE line or the next; WHEN indented from CASE, aligned to
    /// it, or aligned to the first WHEN; THEN on the WHEN line or its own (indented from WHEN, to
    /// WHEN, to the WHEN expression); the THEN / ELSE result on its own line; ELSE and END on their
    /// own lines, aligned per the options.
    /// </summary>
    private void CaseBody(ScalarExpression e)
    {
        var c = S.Case;
        var caseColumn = W.Column;
        W.Through(e.FirstTokenIndex); // CASE

        var input = (e as SimpleCaseExpression)?.InputExpression;
        if (input is not null)
        {
            W.Space();
            Scalar(input);
        }

        IList<WhenClause> whens = e switch
        {
            SearchedCaseExpression s => s.WhenClauses.Cast<WhenClause>().ToList(),
            SimpleCaseExpression s => s.WhenClauses.Cast<WhenClause>().ToList(),
            _ => [],
        };
        var elseExpression = e switch
        {
            SearchedCaseExpression s => s.ElseExpression,
            SimpleCaseExpression s => s.ElseExpression,
            _ => null,
        };

        var firstWhenOnNewLine = c.PlaceFirstWhenOnNewLine switch
        {
            "never" => false,
            "ifInputExpression" => input is not null,
            _ => true,
        };
        var whenColumn = c.WhenAlignment switch
        {
            "toCase" => caseColumn,
            // the first item after CASE: its input expression, or where a WHEN on the CASE line would start
            "toFirstItem" => caseColumn + T[e.FirstTokenIndex].Text.Length + 1,
            _ => caseColumn + Tab,
        };
        var thenColumn = whenColumn + Tab;

        for (var i = 0; i < whens.Count; i++)
        {
            var when = whens[i];
            W.Until(when.FirstTokenIndex);
            if (i == 0)
            {
                if (firstWhenOnNewLine) W.NewLine(whenColumn);
                else W.Space();
                if (c.WhenAlignment == "toFirstItem") whenColumn = W.Column;
            }
            else
            {
                W.NewLine(whenColumn);
            }
            var thisWhen = W.Column;
            W.Through(when.FirstTokenIndex); // WHEN
            W.Space();
            var conditionColumn = W.Column;
            switch (when)
            {
                case SearchedWhenClause sw: Boolean(sw.WhenExpression, new BoolContext(thisWhen, 4, conditionColumn)); break;
                case SimpleWhenClause sw: Scalar(sw.WhenExpression); break;
            }

            var then = FindLast(W.NextToken, when.ThenExpression.FirstTokenIndex - 1, TSqlTokenType.Then);
            if (then >= 0)
            {
                W.Until(then);
                if (c.PlaceThenOnNewLine)
                {
                    thenColumn = c.ThenAlignment switch
                    {
                        "toWhen" => thisWhen,
                        "toWhenExpression" => conditionColumn,
                        _ => thisWhen + Tab,
                    };
                    W.NewLine(thenColumn);
                }
                else
                {
                    W.Space();
                }
                W.Through(then);
            }
            var resultBase = c.PlaceThenOnNewLine ? thenColumn : thisWhen;
            if (c.PlaceExpressionOnNewLine) W.NewLine(resultBase + Tab);
            else W.Space();
            Scalar(when.ThenExpression);
        }

        if (elseExpression is not null)
        {
            var elseToken = FindLast(W.NextToken, elseExpression.FirstTokenIndex - 1, TSqlTokenType.Else);
            if (elseToken >= 0)
            {
                W.Until(elseToken);
                int elseColumn;
                if (c.PlaceElseOnNewLine)
                {
                    elseColumn = c.AlignElseToWhen ? whenColumn : c.PlaceThenOnNewLine ? thenColumn : whenColumn + Tab;
                    W.NewLine(elseColumn);
                }
                else
                {
                    W.Space();
                    elseColumn = W.Column;
                }
                W.Through(elseToken);
                if (c.PlaceExpressionOnNewLine) W.NewLine(elseColumn + Tab);
                else W.Space();
            }
            Scalar(elseExpression);
        }

        var end = e.LastTokenIndex;
        W.Until(end);
        if (c.PlaceEndOnNewLine)
        {
            W.NewLine(c.EndAlignment switch
            {
                "toWhen" => whenColumn,
                "rightAlignedToWhen" => whenColumn + 1,
                _ => caseColumn,
            });
        }
        else
        {
            W.Space();
        }
        W.Through(end);
    }

    // ── boolean expressions ──────────────────────────────────────────────────

    /// <summary>
    /// Where a condition list sits: the keyword that owns it (WHERE, ON, WHEN, IF, "("), that
    /// keyword's width, and the column of the first condition — what AND / OR align against.
    /// </summary>
    private readonly record struct BoolContext(int KeywordColumn, int KeywordWidth, int FirstColumn);

    /// <summary>Comparison operator column set by an aligned AND / OR chain for its operands.</summary>
    private int? _comparisonColumn;

    private void Boolean(BooleanExpression e, BoolContext? context)
    {
        switch (e)
        {
            case BooleanBinaryExpression:
                AndOr(e, context ?? new BoolContext(W.Column, 0, W.Column));
                break;
            case BooleanComparisonExpression c:
                Comparison(c);
                break;
            case BooleanParenthesisExpression p:
                Parens(p.FirstTokenIndex, p.LastTokenIndex, GlobalParens(), column =>
                {
                    var first = W.Column;
                    Boolean(p.Expression, new BoolContext(Math.Min(column, first), 0, first));
                });
                break;
            case BooleanNotExpression n:
                W.Until(n.Expression.FirstTokenIndex); // NOT
                W.Space();
                Boolean(n.Expression, context);
                break;
            case BooleanIsNullExpression isNull:
                Scalar(isNull.Expression);
                W.Space();
                W.Through(isNull.LastTokenIndex);
                break;
            case BooleanTernaryExpression between:
                Between(between);
                break;
            case InPredicate inPredicate:
                In(inPredicate);
                break;
            case LikePredicate like:
                Scalar(like.FirstExpression);
                W.Space();
                W.Until(like.SecondExpression.FirstTokenIndex); // [NOT] LIKE
                W.Space();
                Scalar(like.SecondExpression);
                if (like.EscapeExpression is not null)
                {
                    W.Space();
                    W.Until(like.EscapeExpression.FirstTokenIndex);
                    W.Space();
                    Scalar(like.EscapeExpression);
                }
                break;
            case ExistsPredicate exists:
            {
                var open = Find(exists.FirstTokenIndex, exists.LastTokenIndex, TSqlTokenType.LeftParenthesis);
                W.Until(open); // EXISTS
                BeforeParen();
                Subquery(open, MatchingParen(open), exists.Subquery.QueryExpression);
                break;
            }
            case SubqueryComparisonPredicate sc:
            {
                Scalar(sc.Expression);
                var open = Find(sc.Expression.LastTokenIndex + 1, sc.LastTokenIndex, TSqlTokenType.LeftParenthesis);
                var op = NextSignificant(sc.Expression.LastTokenIndex + 1);
                W.SpaceIf(S.Operators.SpacesAroundComparison);
                W.Through(op);
                if (NextSignificant(op + 1) is var op2 && op2 >= 0 && T[op2].TokenType is TSqlTokenType.EqualsSign or TSqlTokenType.GreaterThan)
                {
                    W.NoSpace();
                    W.Through(op2);
                }
                W.SpaceIf(S.Operators.SpacesAroundComparison);
                W.Until(open); // ANY / ALL / SOME
                BeforeParen();
                Subquery(open, MatchingParen(open), sc.Subquery.QueryExpression);
                break;
            }
            default:
                W.Through(e.LastTokenIndex);
                break;
        }
        W.Through(e.LastTokenIndex);
    }

    private void Comparison(BooleanComparisonExpression c)
    {
        var alignAt = _comparisonColumn;
        _comparisonColumn = null;
        Scalar(c.FirstExpression);
        var op = NextSignificant(c.FirstExpression.LastTokenIndex + 1);
        W.Until(op);
        if (alignAt is int column && !W.IsFlat) W.PadTo(column);
        else W.SpaceIf(S.Operators.SpacesAroundComparison);
        // The operator: ">=", "<>", "!=" are two tokens that must stay together.
        var opLast = LastSignificantBefore(c.SecondExpression.FirstTokenIndex);
        W.Through(op);
        for (var k = op + 1; k <= opLast; k++)
        {
            W.NoSpace();
            W.Through(k);
        }
        W.SpaceIf(S.Operators.SpacesAroundComparison);
        Scalar(c.SecondExpression);
    }

    /// <summary>
    /// A chain of AND / OR conditions. "Place on new line" decides whether each operator starts
    /// a line; alignment puts it at the owning keyword (left-aligned), right-aligned to that
    /// keyword, just before the first condition, at the first condition, or one tab in;
    /// "keyword before condition" puts the operator at the start of the line or the end of the
    /// previous one. "Align comparison operators" lines up the = / &lt; / &gt; across the lines.
    /// </summary>
    private void AndOr(BooleanExpression e, BoolContext ctx)
    {
        var operands = new List<BooleanExpression>();
        var operators = new List<int>();
        void Flatten(BooleanExpression x)
        {
            if (x is BooleanBinaryExpression b)
            {
                Flatten(b.FirstExpression);
                operators.Add(NextSignificant(b.FirstExpression.LastTokenIndex + 1));
                Flatten(b.SecondExpression);
            }
            else
            {
                operands.Add(x);
            }
        }
        Flatten(e);

        var o = S.Operators;

        void Inline()
        {
            Boolean(operands[0], null);
            for (var i = 0; i < operators.Count; i++)
            {
                W.Until(operators[i]);
                W.Space();
                W.Through(operators[i]);
                W.Space();
                Boolean(operands[i + 1], null);
            }
        }

        if (W.IsFlat || o.AndOrPlaceOnNewLine == "never")
        {
            Inline();
            return;
        }
        if (o.AndOrPlaceOnNewLine == "ifLongerThanMaxLineLength" && W.TryOneLine(Inline)) return;

        var first = ctx.FirstColumn;
        int OperatorColumn(int width) => o.AndOrAlignment switch
        {
            "rightAligned" => Math.Max(0, ctx.KeywordColumn + ctx.KeywordWidth - width),
            "beforeFirstListItem" => Math.Max(0, first - width - 1),
            "toFirstListItem" => first,
            "indented" => ctx.KeywordColumn + Tab,
            _ => ctx.KeywordColumn, // leftAligned
        };
        var conditionColumn = o.AndOrAlignment switch
        {
            "leftAligned" => ctx.KeywordColumn,
            "indented" => ctx.KeywordColumn + Tab,
            _ => first,
        };

        // Where each condition will start, for aligning comparison operators.
        int? comparisonColumn = null;
        if (o.AlignComparison)
        {
            var target = 0;
            for (var i = 0; i < operands.Count; i++)
            {
                if (operands[i] is not BooleanComparisonExpression cmp) continue;
                var start = i == 0 ? first
                    : o.AndOrBeforeCondition ? OperatorColumn(T[operators[i - 1]].Text.Length) + T[operators[i - 1]].Text.Length + 1
                    : conditionColumn;
                var width = W.MeasureFlat(() => Scalar(cmp.FirstExpression), cmp.FirstExpression.FirstTokenIndex);
                if (width >= SqlWriter.Unbounded) continue;
                target = Math.Max(target, start + width + 1);
            }
            if (target > 0) comparisonColumn = target;
        }

        for (var i = 0; i < operands.Count; i++)
        {
            if (i > 0)
            {
                var op = operators[i - 1];
                W.Until(op);
                if (o.AndOrBeforeCondition)
                {
                    W.NewLine(OperatorColumn(T[op].Text.Length));
                    W.Through(op);
                    W.Space();
                }
                else
                {
                    W.Space();
                    W.Through(op);
                    W.NewLine(conditionColumn);
                }
            }
            _comparisonColumn = operands[i] is BooleanComparisonExpression ? comparisonColumn : null;
            Boolean(operands[i], null);
            _comparisonColumn = null;
        }
    }

    /// <summary>BETWEEN on the expression line or the next; its AND on the same line or its own, aligned per the options.</summary>
    private void Between(BooleanTernaryExpression b)
    {
        var o = S.Operators;
        var start = W.Column;
        Scalar(b.FirstExpression);

        var keyword = NextSignificant(b.FirstExpression.LastTokenIndex + 1);          // [NOT] BETWEEN
        var keywordLast = LastSignificantBefore(b.SecondExpression.FirstTokenIndex);
        W.Until(keyword);
        if (o.BetweenOnNewLine) W.NewLine(start + Tab);
        else W.Space();
        var betweenColumn = W.Column;
        var betweenWidth = PhraseWidth(keyword, keywordLast);
        W.Through(keywordLast);
        W.Space();
        Scalar(b.SecondExpression);

        var and = LastSignificantBefore(b.ThirdExpression.FirstTokenIndex);
        W.Until(and);
        if (o.BetweenAndOnNewLine)
        {
            W.NewLine(o.BetweenAndAlignment switch
            {
                "rightAlignedToBetween" => betweenColumn + betweenWidth - T[and].Text.Length,
                "toBeginningOfExpression" => start,
                _ => betweenColumn,
            });
        }
        else
        {
            W.Space();
        }
        W.Through(and);
        W.Space();
        Scalar(b.ThirdExpression);
    }

    /// <summary>
    /// IN (values): spaces around the contents; the "(" on the expression line or the next; the
    /// first value after "(" or on the next line; subsequent values on new lines — left-aligned
    /// with the first, right-aligned on their last character, or indented.
    /// </summary>
    private void In(InPredicate p)
    {
        var o = S.Operators;
        var start = W.Column;
        Scalar(p.Expression);
        var open = Find(p.Expression.LastTokenIndex + 1, p.LastTokenIndex, TSqlTokenType.LeftParenthesis);
        var close = MatchingParen(open);
        W.Space();
        W.Until(open); // [NOT] IN

        if (p.Subquery is not null)
        {
            BeforeParen();
            Subquery(open, close, p.Subquery.QueryExpression);
            return;
        }

        if (o.InOpenParenOnNewLine && !W.IsFlat) W.NewLine(o.InAlignment == "indented" ? start + Tab : start);
        else BeforeParen();
        var parenColumn = W.Column;
        W.Through(open);
        W.SpaceIf(o.InSpaceAroundContents);

        var values = p.Values;
        var multi = values.Count > 1;
        var widths = values.Select(v => W.MeasureFlat(() => Scalar(v), v.FirstTokenIndex)).ToList();
        var firstNew = o.InFirstValueOnNewLine switch
        {
            "always" => true,
            "ifSubsequentValues" => multi,
            "ifLongerThanMaxLineLength" => !FitsOnLine(values, W.Column),
            _ => false,
        };
        if (W.IsFlat) firstNew = false;

        var valueColumn = o.InAlignment switch
        {
            "indented" => start + Tab,
            _ => parenColumn + 1 + (o.InSpaceAroundContents ? 1 : 0),
        };
        if (firstNew) W.NewLine(valueColumn);
        var firstColumn = W.Column;
        var alignedColumn = firstNew || o.InAlignment != "indented" ? firstColumn : valueColumn;
        var right = o.InAlignment == "rightAligned" ? firstColumn + widths.Where(w => w < SqlWriter.Unbounded).DefaultIfEmpty(0).Max() : 0;

        void Print(bool breakValues)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    var comma = Find(values[i - 1].LastTokenIndex + 1, values[i].FirstTokenIndex - 1, TSqlTokenType.Comma);
                    W.Until(comma);
                    var column = right > 0 && widths[i] < SqlWriter.Unbounded ? Math.Max(alignedColumn, right - widths[i]) : alignedColumn;
                    if (breakValues && S.Lists.CommasBeforeItems)
                    {
                        W.HoistTrailingComments(comma);
                        W.NewLine(Math.Max(0, column - (S.Lists.SpaceAfterComma ? 2 : 1)));
                        W.Through(comma);
                        W.SpaceIf(S.Lists.SpaceAfterComma);
                    }
                    else
                    {
                        W.SpaceIf(S.Lists.SpaceBeforeComma);
                        W.Through(comma);
                        if (breakValues) W.NewLine(column);
                        else W.SpaceIf(S.Lists.SpaceAfterComma);
                    }
                }
                else if (right > 0 && breakValues && widths[0] < SqlWriter.Unbounded && right - widths[0] > W.Column)
                {
                    W.PadTo(right - widths[0]);
                }
                Scalar(values[i]);
            }
        }

        var printed = false;
        bool breakAll;
        switch (o.InSubsequentValuesOnNewLines)
        {
            case "always":
                breakAll = multi && !W.IsFlat;
                break;
            case "never":
                breakAll = false;
                break;
            default:
                if (!multi || W.IsFlat) breakAll = false;
                else if (W.TryOneLine(() => Print(false))) { printed = true; breakAll = false; }
                else breakAll = true;
                break;
        }
        if (!printed) Print(breakAll);

        W.Until(close);
        W.SpaceIf(o.InSpaceAroundContents);
        W.Through(close);
    }
}
