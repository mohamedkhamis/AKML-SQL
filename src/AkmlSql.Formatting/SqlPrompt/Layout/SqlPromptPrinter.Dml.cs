using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

internal sealed partial class SqlPromptPrinter
{
    // ── INSERT ───────────────────────────────────────────────────────────────

    private void PrintInsert(InsertStatement s, int column)
    {
        if (s.WithCtesAndXmlNamespaces is { } with)
        {
            Ctes(with, column);
            W.NewLine(column);
        }
        var spec = s.InsertSpecification;
        var target = spec.Target;

        // INSERT [TOP (n)] [INTO]
        if (spec.TopRowFilter is { } top)
        {
            W.Through(spec.FirstTokenIndex);
            Modifiers(top.FirstTokenIndex, top.LastTokenIndex, top);
        }
        W.Until(target.FirstTokenIndex);
        if (S.Dml.PlaceInsertTableOnNewLine) W.NewLine(column + Tab);
        else W.Space();
        W.Through(target.LastTokenIndex);

        if (spec.Columns.Count > 0)
        {
            var open = Find(target.LastTokenIndex + 1, spec.Columns[0].FirstTokenIndex - 1, TSqlTokenType.LeftParenthesis);
            if (open >= 0)
            {
                var close = MatchingParen(open);
                var ins = S.Insert;
                W.Until(open);
                BeforeParen();
                Parens(open, close, new ParenSpec(ins.ColumnsParenthesisStyle, ins.ColumnsIndentContents, S.Parentheses.SpacesInside, 0),
                    c => List(spec.Columns, new ListLayout
                    {
                        KeywordColumn = c,
                        StatementColumn = column,
                        FirstOnNewLine = "never",
                        Subsequent = ins.PlaceSubsequentColumnsOnNewLines,
                        AlignToFirst = false, // continuation lines at the content column (indented contents hang)
                        Indent = false,
                        AlignComments = S.Lists.AlignComments,
                    }, (col, _) => W.Through(col.LastTokenIndex)));
            }
        }

        if (spec.OutputClause is { } output)
        {
            W.NewLine(column);
            W.Through(output.LastTokenIndex);
        }
        if (spec.OutputIntoClause is { } outputInto)
        {
            W.NewLine(column);
            W.Through(outputInto.LastTokenIndex);
        }

        switch (spec.InsertSource)
        {
            case ValuesInsertSource { IsDefaultValues: true } dv:
                W.Space();
                W.Through(dv.LastTokenIndex);
                break;
            case ValuesInsertSource v:
                W.NewLine(column);
                Values(v, column);
                break;
            case SelectInsertSource sel:
                W.NewLine(column);
                Query(sel.Select, column);
                break;
            case { } other:
                W.NewLine(column);
                W.Through(other.LastTokenIndex);
                break;
        }
        TrailingClauses(spec.LastTokenIndex, s.LastTokenIndex, column);
    }

    /// <summary>VALUES (…), (…): each row in the INSERT values parenthesis style, rows one per line.</summary>
    private void Values(ValuesInsertSource v, int column)
    {
        W.Through(v.FirstTokenIndex); // VALUES
        if (v.RowValues.Count == 0) return;
        BeforeParen();
        var rowColumn = W.Column;
        List(v.RowValues, new ListLayout
        {
            KeywordColumn = column,
            StatementColumn = column,
            FirstOnNewLine = "never",
            Subsequent = S.Lists.PlaceSubsequentItemsOnNewLines,
            AlignToFirst = true,
            Indent = S.Lists.IndentListItems,
        }, (row, _) => Row(row));
        _ = rowColumn;
    }

    private void Row(RowValue row)
    {
        var open = row.FirstTokenIndex;
        var close = row.LastTokenIndex;
        if (T[open].TokenType != TSqlTokenType.LeftParenthesis)
        {
            W.Through(row.LastTokenIndex);
            return;
        }
        var ins = S.Insert;
        Parens(open, close, new ParenSpec(ins.ValuesParenthesisStyle, ins.ValuesIndentContents, S.Parentheses.SpacesInside, 0),
            c => List(row.ColumnValues, new ListLayout
            {
                KeywordColumn = c,
                StatementColumn = c,
                FirstOnNewLine = "never",
                Subsequent = ins.PlaceSubsequentValuesOnNewLines,
                AlignToFirst = false, // continuation lines at the content column (indented contents hang)
                Indent = false,
                AlignComments = S.Lists.AlignComments,
            }, (value, _) => Scalar(value)));
    }

    // ── UPDATE ───────────────────────────────────────────────────────────────

    private void PrintUpdate(UpdateStatement s, int column)
    {
        if (s.WithCtesAndXmlNamespaces is { } with)
        {
            Ctes(with, column);
            W.NewLine(column);
        }
        var u = s.UpdateSpecification;
        var clauses = new List<Clause>();
        var update = u.FirstTokenIndex;
        clauses.Add(new Clause(update, update, ctx =>
        {
            W.PadTo(ctx.ItemColumn);
            if (u.TopRowFilter is { } top)
            {
                Modifiers(top.FirstTokenIndex, top.LastTokenIndex, top);
                W.Space();
            }
            W.Through(u.Target.LastTokenIndex);
        }, IsList: false));

        if (u.SetClauses.Count > 0)
        {
            var set = FindLast(u.Target.LastTokenIndex + 1, u.SetClauses[0].FirstTokenIndex - 1, TSqlTokenType.Set);
            if (set >= 0)
                clauses.Add(new Clause(set, set, ctx =>
                    List(u.SetClauses, ClauseList(ctx.KeywordColumn, ctx.StatementColumn, ctx.ItemColumn), (sc, _) => SetItem(sc))));
        }
        if (u.OutputClause is { } output)
            clauses.Add(GenericClause(output.FirstTokenIndex, output.FirstTokenIndex, output.LastTokenIndex));
        if (u.OutputIntoClause is { } outputInto)
            clauses.Add(GenericClause(outputInto.FirstTokenIndex, outputInto.FirstTokenIndex, outputInto.LastTokenIndex));
        if (u.FromClause is { } from)
            clauses.Add(new Clause(from.FirstTokenIndex, from.FirstTokenIndex, ctx => FromBody(from, ctx)));
        if (u.WhereClause is { } where)
            clauses.Add(new Clause(where.FirstTokenIndex, where.FirstTokenIndex,
                ctx => ConditionBody(where.SearchCondition, where.LastTokenIndex, ctx, S.Dml.PlaceWhereConditionOnNewLine)));

        Clauses(clauses, column);
        W.Through(u.LastTokenIndex);
        TrailingClauses(u.LastTokenIndex, s.LastTokenIndex, column);
    }

    private void SetItem(SetClause sc)
    {
        if (sc is AssignmentSetClause a && a.NewValue is not null)
        {
            var target = (TSqlFragment?)a.Column ?? a.Variable;
            if (target is not null && target.LastTokenIndex < a.NewValue.FirstTokenIndex)
            {
                W.Through(target.LastTokenIndex);
                var op = LastSignificantBefore(a.NewValue.FirstTokenIndex);
                W.Until(op);
                W.SpaceIf(S.Operators.SpacesAroundComparison);
                W.Through(op);
                W.SpaceIf(S.Operators.SpacesAroundComparison);
                Scalar(a.NewValue);
            }
        }
        W.Through(sc.LastTokenIndex);
    }

    // ── DELETE ───────────────────────────────────────────────────────────────

    private void PrintDelete(DeleteStatement s, int column)
    {
        if (s.WithCtesAndXmlNamespaces is { } with)
        {
            Ctes(with, column);
            W.NewLine(column);
        }
        var d = s.DeleteSpecification;
        var clauses = new List<Clause>();
        var delete = d.FirstTokenIndex;
        var afterDelete = NextSignificant(delete + 1);
        var keywordLast = d.TopRowFilter is null && afterDelete >= 0 && afterDelete < d.Target.FirstTokenIndex
                          && T[afterDelete].TokenType == TSqlTokenType.From
            ? afterDelete
            : delete;
        clauses.Add(new Clause(delete, keywordLast, ctx =>
        {
            W.PadTo(ctx.ItemColumn);
            if (d.TopRowFilter is { } top)
            {
                Modifiers(top.FirstTokenIndex, top.LastTokenIndex, top);
                W.Space();
            }
            W.Through(d.Target.LastTokenIndex);
        }, IsList: false));

        if (d.OutputClause is { } output)
            clauses.Add(GenericClause(output.FirstTokenIndex, output.FirstTokenIndex, output.LastTokenIndex));
        if (d.OutputIntoClause is { } outputInto)
            clauses.Add(GenericClause(outputInto.FirstTokenIndex, outputInto.FirstTokenIndex, outputInto.LastTokenIndex));
        if (d.FromClause is { } from)
            clauses.Add(new Clause(from.FirstTokenIndex, from.FirstTokenIndex, ctx => FromBody(from, ctx)));
        if (d.WhereClause is { } where)
            clauses.Add(new Clause(where.FirstTokenIndex, where.FirstTokenIndex,
                ctx => ConditionBody(where.SearchCondition, where.LastTokenIndex, ctx, S.Dml.PlaceWhereConditionOnNewLine)));

        Clauses(clauses, column);
        W.Through(d.LastTokenIndex);
        TrailingClauses(d.LastTokenIndex, s.LastTokenIndex, column);
    }

    // ── MERGE ────────────────────────────────────────────────────────────────

    private void PrintMerge(MergeStatement s, int column)
    {
        if (s.WithCtesAndXmlNamespaces is { } with)
        {
            Ctes(with, column);
            W.NewLine(column);
        }
        var m = s.MergeSpecification;
        var merge = m.FirstTokenIndex;
        var afterMerge = NextSignificant(merge + 1);
        var mergeLast = afterMerge >= 0 && T[afterMerge].TokenType == TSqlTokenType.Into ? afterMerge : merge;
        var targetLast = m.TableAlias?.LastTokenIndex ?? m.Target.LastTokenIndex;

        var clauses = new List<Clause>
        {
            new(merge, mergeLast, ctx =>
            {
                W.PadTo(ctx.ItemColumn);
                W.Through(targetLast);
            }, IsList: false),
        };

        var usingToken = FindWord(targetLast + 1, m.TableReference.FirstTokenIndex - 1, "USING");
        if (usingToken >= 0)
            clauses.Add(new Clause(usingToken, usingToken, ctx =>
            {
                W.PadTo(ctx.ItemColumn);
                Table(m.TableReference, new FromContext(ctx.KeywordColumn, ctx.KeywordWidth, ctx.ItemColumn));
            }, IsList: false));

        var on = FindLast(m.TableReference.LastTokenIndex + 1, m.SearchCondition.FirstTokenIndex - 1, TSqlTokenType.On);
        if (on >= 0)
            clauses.Add(new Clause(on, on, ctx => ConditionBody(m.SearchCondition, m.SearchCondition.LastTokenIndex, ctx, "never")));

        Clauses(clauses, column);

        foreach (var action in m.ActionClauses)
        {
            W.NewLine(column);
            W.Through(action.FirstTokenIndex); // WHEN
            if (action.SearchCondition is { } condition)
            {
                W.Until(condition.FirstTokenIndex); // [NOT] MATCHED [BY …] AND
                W.Space();
                Boolean(condition, null);
            }
            var then = FindLast(W.NextToken, action.Action.FirstTokenIndex - 1, TSqlTokenType.Then);
            if (then >= 0)
            {
                W.Until(then);
                W.Space();
                W.Through(then);
            }
            W.NewLine(column + Tab);
            MergeActionItem(action.Action, column + Tab);
        }

        if (m.OutputClause is { } output)
        {
            W.NewLine(column);
            W.Through(output.LastTokenIndex);
        }
        if (m.OutputIntoClause is { } outputInto)
        {
            W.NewLine(column);
            W.Through(outputInto.LastTokenIndex);
        }
        W.Through(m.LastTokenIndex);
        TrailingClauses(m.LastTokenIndex, s.LastTokenIndex, column);
    }

    private void MergeActionItem(MergeAction action, int column)
    {
        switch (action)
        {
            case UpdateMergeAction u when u.SetClauses.Count > 0:
            {
                var set = FindLast(u.FirstTokenIndex, u.SetClauses[0].FirstTokenIndex - 1, TSqlTokenType.Set);
                W.Through(u.FirstTokenIndex); // UPDATE
                if (set >= 0)
                {
                    W.NewLine(column);
                    W.Through(set);
                }
                List(u.SetClauses, ClauseList(column, column, TabStop(column + 4)), (sc, _) => SetItem(sc));
                break;
            }
            case InsertMergeAction i:
            {
                W.Through(i.FirstTokenIndex); // INSERT
                if (i.Columns.Count > 0)
                {
                    var open = Find(i.FirstTokenIndex, i.Columns[0].FirstTokenIndex - 1, TSqlTokenType.LeftParenthesis);
                    if (open >= 0)
                    {
                        var close = MatchingParen(open);
                        var ins = S.Insert;
                        W.Until(open);
                        BeforeParen();
                        Parens(open, close, new ParenSpec(ins.ColumnsParenthesisStyle, ins.ColumnsIndentContents, S.Parentheses.SpacesInside, 0),
                            c => List(i.Columns, new ListLayout
                            {
                                KeywordColumn = c,
                                StatementColumn = column,
                                FirstOnNewLine = "never",
                                Subsequent = ins.PlaceSubsequentColumnsOnNewLines,
                                AlignToFirst = false, // continuation lines at the content column (indented contents hang)
                                Indent = false,
                            }, (col, _) => W.Through(col.LastTokenIndex)));
                    }
                }
                if (i.Source is ValuesInsertSource { IsDefaultValues: false } v)
                {
                    W.NewLine(column);
                    Values(v, column);
                }
                break;
            }
        }
        W.Through(action.LastTokenIndex);
    }
}
