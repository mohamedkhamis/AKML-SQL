using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

internal sealed partial class SqlPromptPrinter
{
    // ── SELECT ───────────────────────────────────────────────────────────────

    private void PrintSelect(SelectStatement s, int column)
    {
        if (s.WithCtesAndXmlNamespaces is { } with)
        {
            Ctes(with, column);
            W.NewLine(column);
        }
        Query(s.QueryExpression, column);
        TrailingClauses(s.QueryExpression.LastTokenIndex, s.LastTokenIndex, column);
    }

    /// <summary>OPTION (…), COMPUTE…, FOR BROWSE: whatever follows the query, one clause per line.</summary>
    private void TrailingClauses(int after, int statementLast, int column)
    {
        var last = T[statementLast].TokenType == TSqlTokenType.Semicolon ? statementLast - 1 : statementLast;
        var next = NextSignificant(after + 1);
        if (next < 0 || next > last) return;
        W.NewLine(column);
        W.Through(last);
    }

    private void Query(QueryExpression q, int column)
    {
        switch (q)
        {
            case QuerySpecification qs:
                QuerySpec(qs, column);
                break;

            case BinaryQueryExpression b:
                Query(b.FirstQueryExpression, column);
                W.NewLine(column);
                W.Until(b.SecondQueryExpression.FirstTokenIndex);   // UNION [ALL] / EXCEPT / INTERSECT
                W.NewLine(column);
                Query(b.SecondQueryExpression, column);
                QueryTail(b, column);
                break;

            case QueryParenthesisExpression p:
            {
                var open = p.FirstTokenIndex;
                var close = MatchingParen(open);
                Parens(open, close, GlobalParens(subquery: true), c => Query(p.QueryExpression, c));
                QueryTail(p, column);
                break;
            }

            default:
                W.Through(q.LastTokenIndex);
                break;
        }
    }

    /// <summary>ORDER BY / OFFSET / FOR that apply to a whole UNION or parenthesized query.</summary>
    private void QueryTail(QueryExpression q, int column)
    {
        var clauses = new List<Clause>();
        if (q.OrderByClause is { } o) clauses.Add(OrderByLayout(o));
        if (q.OffsetClause is { } off) clauses.Add(GenericClause(off.FirstTokenIndex, off.FirstTokenIndex, off.LastTokenIndex));
        if (q.ForClause is { } f) clauses.Add(GenericClause(ClauseKeyword(f, TSqlTokenType.For), ClauseKeyword(f, TSqlTokenType.For), f.LastTokenIndex));
        if (clauses.Count == 0) return;
        W.NewLine(column);
        Clauses(clauses, column);
    }

    private int ClauseKeyword(TSqlFragment f, TSqlTokenType type)
    {
        var i = FindLast(0, f.FirstTokenIndex, type);
        return i >= 0 ? i : f.FirstTokenIndex;
    }

    private void QuerySpec(QuerySpecification qs, int column)
    {
        var clauses = new List<Clause>();
        var select = qs.FirstTokenIndex;
        clauses.Add(new Clause(select, select, ctx => SelectListLayout(qs, ctx)));

        // SELECT … INTO target: the INTO clause sits between the list and FROM.
        var afterList = qs.SelectElements.Count > 0 ? qs.SelectElements[^1].LastTokenIndex : select;
        var nextClause = new int?[]
        {
            qs.FromClause?.FirstTokenIndex, qs.WhereClause?.FirstTokenIndex, qs.GroupByClause?.FirstTokenIndex,
            qs.HavingClause?.FirstTokenIndex, qs.OrderByClause?.FirstTokenIndex, qs.OffsetClause?.FirstTokenIndex,
        }.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty(qs.LastTokenIndex + 1).Min();
        var into = Find(afterList + 1, nextClause - 1, TSqlTokenType.Into);
        if (into >= 0)
            clauses.Add(GenericClause(into, into, nextClause - 1));

        if (qs.FromClause is { } from)
            clauses.Add(new Clause(from.FirstTokenIndex, from.FirstTokenIndex, ctx => FromBody(from, ctx)));
        if (qs.WhereClause is { } where)
            clauses.Add(new Clause(where.FirstTokenIndex, where.FirstTokenIndex,
                ctx => ConditionBody(where.SearchCondition, where.LastTokenIndex, ctx, S.Dml.PlaceWhereConditionOnNewLine)));
        if (qs.GroupByClause is { } group)
            clauses.Add(GroupByLayout(group));
        if (qs.HavingClause is { } having)
            clauses.Add(new Clause(having.FirstTokenIndex, having.FirstTokenIndex,
                ctx => ConditionBody(having.SearchCondition, having.LastTokenIndex, ctx, "never")));
        if (qs.WindowClause is { } window)
            clauses.Add(GenericClause(window.FirstTokenIndex, window.FirstTokenIndex, window.LastTokenIndex));
        if (qs.OrderByClause is { } order)
            clauses.Add(OrderByLayout(order));
        if (qs.OffsetClause is { } offset)
            clauses.Add(GenericClause(offset.FirstTokenIndex, offset.FirstTokenIndex, offset.LastTokenIndex));
        if (qs.ForClause is { } forClause)
        {
            var forTok = FindLast(qs.FirstTokenIndex, forClause.FirstTokenIndex, TSqlTokenType.For);
            if (forTok >= 0) clauses.Add(GenericClause(forTok, forTok, forClause.LastTokenIndex));
        }

        Clauses(clauses, column);
        W.Through(qs.LastTokenIndex);
    }

    /// <summary>A clause whose body is emitted as written, starting at the clause's item column.</summary>
    private Clause GenericClause(int keywordFirst, int keywordLast, int last) =>
        new(keywordFirst, keywordLast, ctx =>
        {
            if (NextSignificant(keywordLast + 1) is var n && n >= 0 && n <= last) W.PadTo(ctx.ItemColumn);
            W.Through(last);
        }, IsList: false);

    private void SelectListLayout(QuerySpecification qs, ClauseContext ctx)
    {
        var select = qs.FirstTokenIndex;
        if (qs.SelectElements.Count == 0) return;
        var firstItem = qs.SelectElements[0].FirstTokenIndex;
        int? firstColumn = ctx.ItemColumn;
        string? firstOnNewLine = null;

        // DISTINCT / ALL / TOP (n) [PERCENT] [WITH TIES]
        var modifier = NextSignificant(select + 1);
        if (modifier >= 0 && modifier < firstItem)
        {
            if (S.Dml.PlaceDistinctAndTopOnNewLine)
                W.NewLine(ctx.KeywordColumn + (S.Lists.IndentListItems ? Tab : 0));
            else
                W.PadTo(ctx.ItemColumn);
            Modifiers(modifier, firstItem - 1, qs.TopRowFilter);

            if (S.Dml.NewLineAfterDistinctAndTop)
            {
                W.NewLine(S.Dml.PlaceDistinctAndTopOnNewLine ? ctx.KeywordColumn + (S.Lists.IndentListItems ? Tab : 0) : ctx.ItemColumn);
                firstOnNewLine = "never";
            }
            else
            {
                W.Space();
                firstOnNewLine = "never";
            }
            firstColumn = null;
        }

        int? aliasColumn = null;
        List(qs.SelectElements, ClauseList(ctx.KeywordColumn, ctx.StatementColumn, firstColumn, firstOnNewLine,
                onLayout: (startOf, broken) => aliasColumn = broken && S.Lists.AlignAliases ? AliasColumn(qs.SelectElements, startOf) : null),
            (e, _) => SelectItem(e, aliasColumn));
    }

    /// <summary>DISTINCT / TOP between SELECT and the first item: "TOP (10)" keeps its parenthesis spacing.</summary>
    private void Modifiers(int first, int last, TopRowFilter? top)
    {
        if (top is not null)
        {
            W.Until(top.FirstTokenIndex);
            if (W.NextToken > first) W.Space();
            W.Through(top.FirstTokenIndex); // TOP
            var open = Find(top.FirstTokenIndex + 1, top.LastTokenIndex, TSqlTokenType.LeftParenthesis);
            if (open >= 0)
            {
                var close = MatchingParen(open);
                BeforeParen();
                W.Through(open);
                W.SpaceIf(S.Parentheses.SpacesInside);
                Scalar(top.Expression);
                W.Until(close);
                W.SpaceIf(S.Parentheses.SpacesInside);
                W.Through(close);
            }
            else
            {
                W.Space();
                Scalar(top.Expression);
            }
        }
        W.Through(last);
    }

    /// <summary>The column aliases line up at: one past the widest single-line expression.</summary>
    private int? AliasColumn(IList<SelectElement> items, Func<int, int> startOf)
    {
        int? column = null;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not SelectScalarExpression { ColumnName: not null } sse ||
                sse.ColumnName.FirstTokenIndex < sse.Expression.FirstTokenIndex) continue;
            var start = startOf(i);
            if (start < 0) continue;
            var width = W.MeasureFlat(() => Scalar(sse.Expression), sse.Expression.FirstTokenIndex);
            if (width >= SqlWriter.Unbounded || start + width > Max) continue;
            column = Math.Max(column ?? 0, start + width + 1);
        }
        return column;
    }

    private void SelectItem(SelectElement e, int? aliasColumn)
    {
        switch (e)
        {
            case SelectScalarExpression sse when sse.ColumnName is not null && sse.ColumnName.FirstTokenIndex < sse.Expression.FirstTokenIndex:
            {
                // alias = expression
                W.Through(sse.ColumnName.LastTokenIndex);
                var eq = Find(sse.ColumnName.LastTokenIndex + 1, sse.Expression.FirstTokenIndex - 1, TSqlTokenType.EqualsSign);
                if (eq >= 0)
                {
                    W.Until(eq);
                    W.SpaceIf(S.Operators.SpacesAroundComparison);
                    W.Through(eq);
                    W.SpaceIf(S.Operators.SpacesAroundComparison);
                }
                Scalar(sse.Expression);
                break;
            }
            case SelectScalarExpression sse:
                Scalar(sse.Expression);
                if (sse.ColumnName is not null)
                {
                    if (aliasColumn is int column && !W.IsFlat) W.PadTo(column);
                    else W.Space();
                    W.Through(sse.ColumnName.LastTokenIndex);
                }
                break;
            case SelectSetVariable ssv:
                W.Through(ssv.Variable.LastTokenIndex);
                W.SpaceIf(S.Operators.SpacesAroundComparison);
                W.Until(ssv.Expression.FirstTokenIndex);
                W.SpaceIf(S.Operators.SpacesAroundComparison);
                Scalar(ssv.Expression);
                break;
            default:
                W.Through(e.LastTokenIndex);
                break;
        }
        W.Through(e.LastTokenIndex);
    }

    // ── FROM and joins ───────────────────────────────────────────────────────

    /// <summary>Where FROM sits, for aligning JOIN keywords to it.</summary>
    private readonly record struct FromContext(int FromColumn, int FromWidth, int FirstTableColumn);

    private void FromBody(FromClause from, ClauseContext ctx)
    {
        var refs = from.TableReferences;
        var tables = refs.Sum(CountTables);
        var firstNew = S.Dml.PlaceFromTableOnNewLine switch
        {
            "always" => true,
            "ifMultiple" => tables > 1,
            _ => false,
        };
        var layout = new ListLayout
        {
            KeywordColumn = ctx.KeywordColumn,
            StatementColumn = ctx.StatementColumn,
            FirstColumn = ctx.ItemColumn,
            FirstOnNewLine = firstNew ? "always" : "never",
            Subsequent = S.Lists.PlaceSubsequentItemsOnNewLines,
            AlignToFirst = S.Lists.AlignSubsequentItemsWithFirstItem,
            Indent = S.Lists.IndentListItems,
            AlignComments = S.Lists.AlignComments,
        };
        int? firstTable = null;
        List(refs, layout, (t, _) =>
        {
            firstTable ??= W.Column;
            Table(t, new FromContext(ctx.KeywordColumn, ctx.KeywordWidth, firstTable.Value));
        });
        W.Through(from.LastTokenIndex);
    }

    private static int CountTables(TableReference t) => t switch
    {
        JoinTableReference j => CountTables(j.FirstTableReference) + CountTables(j.SecondTableReference),
        JoinParenthesisTableReference p => CountTables(p.Join),
        _ => 1,
    };

    private void Table(TableReference t, FromContext from)
    {
        switch (t)
        {
            case JoinTableReference j:
                Join(j, from);
                break;
            case JoinParenthesisTableReference p:
            {
                var open = p.FirstTokenIndex;
                var close = MatchingParen(open);
                var width = from.FromWidth;
                Parens(open, close, GlobalParens(), c => Table(p.Join, new FromContext(c, width, c)));
                break;
            }
            case QueryDerivedTable d:
            {
                var open = d.FirstTokenIndex;
                var close = MatchingParen(open);
                if (T[open].TokenType == TSqlTokenType.LeftParenthesis && close > 0)
                {
                    Subquery(open, close, d.QueryExpression);
                    if (close < d.LastTokenIndex) W.Space();
                }
                W.Through(d.LastTokenIndex);
                break;
            }
            default:
                W.Through(t.LastTokenIndex);
                break;
        }
    }

    /// <summary>
    /// JOIN placement (new line, aligned to FROM / right-aligned to FROM / to the first table /
    /// indented), the joined table (same line or its own, indented or not), and ON (same line or
    /// its own, aligned to JOIN / right-aligned / to the table / indented) with its condition.
    /// </summary>
    private void Join(JoinTableReference j, FromContext from)
    {
        Table(j.FirstTableReference, from);

        var keywordFirst = NextSignificant(j.FirstTableReference.LastTokenIndex + 1);
        var keywordLast = LastSignificantBefore(j.SecondTableReference.FirstTokenIndex);
        var keywordWidth = PhraseWidth(keywordFirst, keywordLast);
        var firstWordWidth = T[keywordFirst].Text.Length;

        W.Until(keywordFirst);
        int joinColumn;
        if (S.Join.PlaceJoinOnNewLine)
        {
            joinColumn = S.Join.JoinAlignment switch
            {
                "rightAlignedToFrom" => Math.Max(0, from.FromColumn + from.FromWidth - keywordWidth),
                "toTable" => from.FirstTableColumn,
                "indented" => from.FromColumn + Tab,
                _ => from.FromColumn,
            };
            W.NewLine(joinColumn, S.Join.EmptyLineBetweenJoins ? 1 : 0);
        }
        else
        {
            W.Space();
            joinColumn = W.Column;
        }
        W.Through(keywordLast);

        if (S.Join.PlaceJoinTableOnNewLine) W.NewLine(joinColumn + (S.Join.IndentJoinTable ? Tab : 0));
        else W.Space();
        var tableColumn = W.Column;
        Table(j.SecondTableReference, from);

        if (j is not QualifiedJoin q) return;

        var on = FindLast(q.SecondTableReference.LastTokenIndex + 1, q.SearchCondition.FirstTokenIndex - 1, TSqlTokenType.On);
        if (on < 0)
        {
            W.Space();
            Boolean(q.SearchCondition, null);
            return;
        }
        W.Until(on);
        int onColumn;
        if (S.Join.PlaceOnOnNewLine)
        {
            onColumn = S.Join.OnAlignment switch
            {
                "rightAlignedToJoin" => Math.Max(0, joinColumn + keywordWidth - 2),
                "rightAlignedToInner" => Math.Max(0, joinColumn + firstWordWidth - 2),
                "toTable" => tableColumn,
                "indented" => joinColumn + Tab,
                _ => joinColumn,
            };
            W.NewLine(onColumn);
        }
        else
        {
            W.Space();
            onColumn = W.Column;
        }
        W.Through(on);

        if (S.Join.PlaceConditionOnNewLine)
        {
            W.NewLine(S.Join.ConditionAlignment switch
            {
                "toInner" => joinColumn,
                "toTable" => tableColumn,
                "indented" => onColumn + Tab,
                _ => onColumn,
            });
        }
        else
        {
            W.Space();
        }
        Boolean(q.SearchCondition, new BoolContext(onColumn, 2, W.Column));
    }

    // ── WHERE / HAVING / GROUP BY / ORDER BY ─────────────────────────────────

    private void ConditionBody(BooleanExpression condition, int last, ClauseContext ctx, string placement)
    {
        var multiple = condition is BooleanBinaryExpression;
        var newLine = placement switch
        {
            "always" => true,
            "ifMultiple" => multiple,
            _ => false,
        };
        if (newLine) W.NewLine(ctx.KeywordColumn + (S.Lists.IndentListItems ? Tab : 0));
        else W.PadTo(ctx.ItemColumn);
        Boolean(condition, new BoolContext(ctx.KeywordColumn, ctx.KeywordWidth, W.Column));
        W.Through(last);
    }

    private Clause GroupByLayout(GroupByClause g)
    {
        var by = NextSignificant(g.FirstTokenIndex + 1);
        return new Clause(g.FirstTokenIndex, by, ctx =>
        {
            var items = g.GroupingSpecifications;
            // GROUP BY ALL
            var first = items.Count > 0 ? items[0].FirstTokenIndex : g.LastTokenIndex + 1;
            if (NextSignificant(by + 1) is var n && n >= 0 && n < first)
            {
                W.PadTo(ctx.ItemColumn);
                W.Until(first);
            }
            List(items, DmlItemList(ctx), (item, _) =>
            {
                if (item is ExpressionGroupingSpecification eg) Scalar(eg.Expression);
                W.Through(item.LastTokenIndex);
            });
            W.Through(g.LastTokenIndex);
        });
    }

    private Clause OrderByLayout(OrderByClause o)
    {
        var by = NextSignificant(o.FirstTokenIndex + 1);
        return new Clause(o.FirstTokenIndex, by, ctx =>
        {
            List(o.OrderByElements, DmlItemList(ctx), (item, _) =>
            {
                Scalar(item.Expression);
                if (item.LastTokenIndex > item.Expression.LastTokenIndex) W.Space();
                W.Through(item.LastTokenIndex);
            });
            W.Through(o.LastTokenIndex);
        });
    }

    /// <summary>GROUP BY / ORDER BY lists: "place GROUP BY and ORDER BY items on new line" decides the first item.</summary>
    private ListLayout DmlItemList(ClauseContext ctx) => new()
    {
        KeywordColumn = ctx.KeywordColumn,
        StatementColumn = ctx.StatementColumn,
        FirstColumn = ctx.ItemColumn,
        FirstOnNewLine = S.Dml.PlaceGroupByAndOrderByOnNewLine,
        Subsequent = S.Lists.PlaceSubsequentItemsOnNewLines,
        AlignToFirst = S.Lists.AlignSubsequentItemsWithFirstItem,
        Indent = S.Lists.IndentListItems,
        AlignComments = S.Lists.AlignComments,
    };

    // ── CTEs ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// WITH name (columns) AS (query), …: name on the WITH line or its own (indented or not),
    /// the column list on the name line or its own (indented / left / right-aligned), AS on the
    /// column line or its own (aligned the same three ways), and the body in the CTE parenthesis style.
    /// </summary>
    private void Ctes(WithCtesAndXmlNamespaces with, int column)
    {
        var ctes = with.CommonTableExpressions;
        if (ctes.Count == 0)
        {
            W.Through(with.LastTokenIndex);
            return;
        }
        var withWidth = T[with.FirstTokenIndex].Text.Length;
        W.Until(ctes[0].FirstTokenIndex);  // WITH [XMLNAMESPACES (…),]

        var firstNameColumn = 0;
        for (var i = 0; i < ctes.Count; i++)
        {
            var cte = ctes[i];
            if (i == 0)
            {
                if (S.Cte.PlaceNameOnNewLine) W.NewLine(S.Cte.IndentName ? column + Tab : column);
                else W.Space();
                firstNameColumn = W.Column;
            }
            else
            {
                var comma = Find(ctes[i - 1].LastTokenIndex + 1, cte.FirstTokenIndex - 1, TSqlTokenType.Comma);
                if (comma >= 0)
                {
                    W.Until(comma);
                    if (S.Lists.CommasBeforeItems)
                    {
                        W.HoistTrailingComments(comma);
                        W.NewLine(S.Lists.CommaAlignment == "toStatement" ? column : Math.Max(0, firstNameColumn - (S.Lists.CommaAlignment == "beforeItem" ? 2 : 0)));
                        W.Through(comma);
                        W.SpaceIf(S.Lists.SpaceAfterComma);
                    }
                    else
                    {
                        W.SpaceIf(S.Lists.SpaceBeforeComma);
                        W.Through(comma);
                        W.NewLine(firstNameColumn);
                    }
                }
                else
                {
                    W.NewLine(firstNameColumn);
                }
            }

            W.Through(cte.ExpressionName.LastTokenIndex);

            if (cte.Columns.Count > 0)
            {
                var open = Find(cte.ExpressionName.LastTokenIndex + 1, cte.Columns[0].FirstTokenIndex - 1, TSqlTokenType.LeftParenthesis);
                var close = MatchingParen(open);
                W.Until(open);
                if (S.Cte.PlaceColumnsOnNewLine)
                {
                    // aligned against WITH: left, one tab in, or right after it
                    W.NewLine(S.Cte.ColumnAlignment switch
                    {
                        "indented" => column + Tab,
                        "rightAligned" => column + withWidth + 1,
                        _ => column,
                    });
                }
                else
                {
                    BeforeParen();
                }
                ColumnListParens(open, close, cte.Columns);
            }

            var asToken = FindLast(cte.ExpressionName.LastTokenIndex + 1, cte.QueryExpression.FirstTokenIndex - 1, TSqlTokenType.As);
            var bodyOpen = Find(asToken + 1, cte.QueryExpression.FirstTokenIndex, TSqlTokenType.LeftParenthesis);
            if (asToken < 0 || bodyOpen < 0)
            {
                W.Through(cte.LastTokenIndex);
                continue;
            }
            W.Until(asToken);
            if (S.Cte.PlaceAsOnNewLine)
            {
                // aligned against WITH: left, one tab in, or right-aligned with it
                W.NewLine(S.Cte.AsAlignment switch
                {
                    "indented" => column + Tab,
                    "rightAligned" => column + Math.Max(0, withWidth - 2),
                    _ => column,
                });
            }
            else
            {
                W.Space();
            }
            W.Through(asToken);

            var bodyClose = MatchingParen(bodyOpen);
            W.Until(bodyOpen);
            W.Space();
            var global = GlobalParens(subquery: true);
            Parens(bodyOpen, bodyClose, new ParenSpec(S.Cte.ParenthesisStyle, S.Cte.IndentContents, S.Parentheses.SpacesInside, global.CollapseBelow),
                c => Query(cte.QueryExpression, c));
            W.Through(cte.LastTokenIndex);
        }
        W.Through(with.LastTokenIndex);
    }

    /// <summary>"(a, b, c)" column lists: on one line when they fit, one column per line aligned otherwise.</summary>
    private void ColumnListParens<TItem>(int open, int close, IList<TItem> columns) where TItem : TSqlFragment
    {
        Parens(open, close, GlobalParens(), content =>
        {
            List(columns, new ListLayout
            {
                KeywordColumn = content,
                StatementColumn = content,
                FirstOnNewLine = "never",
                Subsequent = "ifLongerThanMaxLineLength",
                AlignToFirst = false,
                Indent = false,
            }, (c, _) => W.Through(c.LastTokenIndex));
        });
    }
}
