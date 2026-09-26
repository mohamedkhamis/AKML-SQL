using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

internal sealed partial class SqlPromptPrinter
{
    // ── CREATE TABLE ─────────────────────────────────────────────────────────

    private void CreateTable(CreateTableStatement c, int column)
    {
        if (c.Definition is null)
        {
            GenericStatement(c);
            return;
        }
        var open = Find(c.SchemaObjectName.LastTokenIndex + 1, c.LastTokenIndex, TSqlTokenType.LeftParenthesis);
        if (open < 0)
        {
            GenericStatement(c);
            return;
        }
        var close = MatchingParen(open);
        W.Until(open);
        BeforeParen();
        TableDef(open, close, c.Definition, column);
        DdlTrailingClauses(close, c.LastTokenIndex, column);
    }

    /// <summary>ON [PRIMARY], TEXTIMAGE_ON, WITH (…) after a table definition: each on its own line.</summary>
    private void DdlTrailingClauses(int after, int statementLast, int column)
    {
        var last = T[statementLast].TokenType == TSqlTokenType.Semicolon ? statementLast - 1 : statementLast;
        var depth = 0;
        for (var i = after + 1; i <= last; i++)
        {
            var t = T[i];
            if (t.TokenType == TSqlTokenType.LeftParenthesis) depth++;
            else if (t.TokenType == TSqlTokenType.RightParenthesis) depth--;
            else if (depth == 0 && (t.TokenType is TSqlTokenType.On or TSqlTokenType.With || IsWord(i, "TEXTIMAGE_ON") || IsWord(i, "FILESTREAM_ON")))
            {
                W.Until(i);
                W.NewLine(column + (S.Ddl.IndentClauses ? Tab : 0));
            }
        }
        W.Through(last);
    }

    /// <summary>
    /// "( column definitions, constraints )" in the DDL parenthesis style. Elements go one per line;
    /// with "align data types and constraints" names, types and constraints form columns.
    /// </summary>
    private void TableDef(int open, int close, TableDefinition definition, int column)
    {
        var elements = new List<TSqlFragment>();
        elements.AddRange(definition.ColumnDefinitions);
        elements.AddRange(definition.TableConstraints);
        elements.AddRange(definition.Indexes);
        if (definition.SystemTimePeriod is not null) elements.Add(definition.SystemTimePeriod);
        elements.Sort((a, b) => a.FirstTokenIndex.CompareTo(b.FirstTokenIndex));

        Parens(open, close, DdlParens(), c =>
        {
            int? typeColumn = null, constraintColumn = null;
            List(elements, new ListLayout
            {
                KeywordColumn = c,
                StatementColumn = column,
                FirstOnNewLine = "never",
                Subsequent = "always",
                AlignToFirst = false, // continuation lines at the content column (indented contents hang)
                Indent = false,
                AlignComments = S.Lists.AlignComments,
                OnLayout = (startOf, broken) =>
                {
                    if (!broken || !S.Ddl.AlignDataTypesAndConstraints) return;
                    int names = 0, types = 0;
                    for (var i = 0; i < elements.Count; i++)
                    {
                        if (elements[i] is not ColumnDefinition cd || cd.DataType is null) continue;
                        var start = startOf(i);
                        if (start < 0) continue;
                        names = Math.Max(names, start + PhraseWidth(cd.ColumnIdentifier.FirstTokenIndex, cd.ColumnIdentifier.LastTokenIndex));
                        types = Math.Max(types, W.MeasureFlat(() => DataType(cd.DataType, variable: false), cd.DataType.FirstTokenIndex));
                    }
                    if (names == 0) return;
                    typeColumn = names + 1;
                    constraintColumn = typeColumn + types + 1;
                },
            }, (e, _) => TableElement(e, typeColumn, constraintColumn));
        });
    }

    private void TableElement(TSqlFragment e, int? typeColumn, int? constraintColumn)
    {
        switch (e)
        {
            case ColumnDefinition cd:
                ColumnDef(cd, typeColumn, constraintColumn);
                break;
            case ConstraintDefinition constraint:
                Constraint(constraint);
                break;
        }
        W.Through(e.LastTokenIndex);
    }

    private void ColumnDef(ColumnDefinition cd, int? typeColumn, int? constraintColumn)
    {
        var start = W.Column;
        W.Through(cd.ColumnIdentifier.LastTokenIndex);
        var afterType = cd.ColumnIdentifier.LastTokenIndex;
        if (cd.DataType is not null)
        {
            if (typeColumn is int tc) W.PadTo(tc);
            else W.Space();
            DataType(cd.DataType, variable: false);
            afterType = cd.DataType.LastTokenIndex;
        }
        else if (cd.ComputedColumnExpression is { } computed)
        {
            W.Space();
            W.Until(computed.FirstTokenIndex); // AS
            W.Space();
            Scalar(computed);
            afterType = computed.LastTokenIndex;
        }

        // Constraints and options, in source order: NULL / NOT NULL and IDENTITY stay on the column's
        // line; named and other constraints go on their own lines when the option asks for it.
        var parts = new List<TSqlFragment>();
        if (cd.IdentityOptions is not null) parts.Add(cd.IdentityOptions);
        if (cd.DefaultConstraint is not null) parts.Add(cd.DefaultConstraint);
        parts.AddRange(cd.Constraints);
        parts.Sort((a, b) => a.FirstTokenIndex.CompareTo(b.FirstTokenIndex));

        var first = NextSignificant(afterType + 1);
        if (first < 0 || first > cd.LastTokenIndex) return;
        if (constraintColumn is int cc) W.PadTo(cc);
        else W.Space();
        var firstPart = true;
        foreach (var part in parts)
        {
            if (part.FirstTokenIndex < W.NextToken) continue;
            W.Until(part.FirstTokenIndex);
            var ownLine = S.Ddl.PlaceConstraintsOnNewLines && part is not NullableConstraintDefinition && part is not IdentityOptions;
            if (ownLine && !W.IsFlat) W.NewLine(start + Tab);
            else if (!firstPart) W.Space();
            firstPart = false;
            if (part is ConstraintDefinition constraint) Constraint(constraint);
            W.Through(part.LastTokenIndex);
        }
    }

    /// <summary>A constraint, with its column lists laid out per "place constraint columns on new lines".</summary>
    private void Constraint(ConstraintDefinition c)
    {
        switch (c)
        {
            case UniqueConstraintDefinition u when u.Columns.Count > 0:
            {
                var open = FindLast(u.FirstTokenIndex, u.Columns[0].FirstTokenIndex, TSqlTokenType.LeftParenthesis);
                if (open >= 0) ConstraintColumns(open, MatchingParen(open), u.Columns);
                break;
            }
            case ForeignKeyConstraintDefinition f:
            {
                if (f.Columns.Count > 0)
                {
                    var open = FindLast(f.FirstTokenIndex, f.Columns[0].FirstTokenIndex, TSqlTokenType.LeftParenthesis);
                    if (open >= 0) ConstraintColumns(open, MatchingParen(open), f.Columns);
                }
                if (f.ReferencedTableColumns.Count > 0)
                {
                    var open = FindLast(W.NextToken, f.ReferencedTableColumns[0].FirstTokenIndex, TSqlTokenType.LeftParenthesis);
                    if (open >= 0) ConstraintColumns(open, MatchingParen(open), f.ReferencedTableColumns);
                }
                break;
            }
            case CheckConstraintDefinition check:
            {
                var open = FindLast(check.FirstTokenIndex, check.CheckCondition.FirstTokenIndex, TSqlTokenType.LeftParenthesis);
                if (open >= 0)
                {
                    W.Until(open);
                    BeforeParen();
                    Parens(open, MatchingParen(open), DdlParens(), _ => Boolean(check.CheckCondition, null));
                }
                break;
            }
            case DefaultConstraintDefinition d:
                W.Until(d.Expression.FirstTokenIndex);
                W.Space();
                Scalar(d.Expression);
                break;
        }
        W.Through(c.LastTokenIndex);
    }

    private void ConstraintColumns<TItem>(int open, int close, IList<TItem> columns) where TItem : TSqlFragment
    {
        var mode = S.Ddl.PlaceConstraintColumnsOnNewLines;
        var force = mode == "always" || (mode == "ifLongerOrMultipleColumns" && columns.Count > 1);
        W.Until(open);
        BeforeParen();
        var spec = DdlParens() with { CollapseBelow = 0 };
        if (force && !W.IsFlat)
        {
            ForcedParens(open, close, spec, c => List(columns, new ListLayout
            {
                KeywordColumn = c,
                StatementColumn = c,
                FirstOnNewLine = "never",
                Subsequent = "always",
                AlignToFirst = false, // continuation lines at the content column (indented contents hang)
                Indent = false,
            }, (col, _) => W.Through(col.LastTokenIndex)));
            return;
        }
        Parens(open, close, spec, c => List(columns, new ListLayout
        {
            KeywordColumn = c,
            StatementColumn = c,
            FirstOnNewLine = "never",
            Subsequent = "ifLongerThanMaxLineLength",
            AlignToFirst = false, // continuation lines at the content column (indented contents hang)
            Indent = false,
        }, (col, _) => W.Through(col.LastTokenIndex)));
    }

    /// <summary><see cref="Parens"/> without the one-line shortcut: the style's multi-line geometry, always.</summary>
    private void ForcedParens(int open, int close, ParenSpec spec, Action<int> content)
    {
        _forceParenBreak = true;
        Parens(open, close, spec, content);
    }

    private bool _forceParenBreak;

    // ── ALTER TABLE … ADD ────────────────────────────────────────────────────

    private void AlterTableAdd(AlterTableAddTableElementStatement a, int column)
    {
        var d = a.Definition;
        var elements = new List<TSqlFragment>();
        if (d is not null)
        {
            elements.AddRange(d.ColumnDefinitions);
            elements.AddRange(d.TableConstraints);
            elements.AddRange(d.Indexes);
        }
        if (elements.Count == 0)
        {
            GenericStatement(a);
            return;
        }
        elements.Sort((x, y) => x.FirstTokenIndex.CompareTo(y.FirstTokenIndex));
        W.Until(elements[0].FirstTokenIndex); // ALTER TABLE t [WITH CHECK] ADD
        W.Space();
        List(elements, ClauseList(column, column, null), (e, _) => TableElement(e, null, null));
    }

    // ── procedures, functions, views, triggers ───────────────────────────────

    private void Procedure(ProcedureStatementBody p, int column)
    {
        W.Through(p.ProcedureReference.LastTokenIndex);
        var parameters = p.Parameters;
        var body = p.StatementList?.Statements;
        var bodyFirst = body is { Count: > 0 } ? body[0].FirstTokenIndex : p.LastTokenIndex + 1;
        if (parameters.Count > 0)
        {
            var open = NextSignificant(p.ProcedureReference.LastTokenIndex + 1);
            if (open >= 0 && T[open].TokenType == TSqlTokenType.LeftParenthesis && MatchingParen(open) > parameters[^1].LastTokenIndex)
            {
                BeforeParen();
                Parens(open, MatchingParen(open), DdlParens(), c => Parameters(parameters, column, c));
            }
            else
            {
                Parameters(parameters, column, null);
            }
        }

        var asToken = FindLast(W.NextToken, bodyFirst - 1, TSqlTokenType.As);
        if (asToken < 0)
        {
            W.Through(p.LastTokenIndex);
            return;
        }
        // WITH RECOMPILE / EXECUTE AS … / FOR REPLICATION
        var options = NextSignificant(W.NextToken);
        if (options >= 0 && options < asToken)
        {
            W.NewLine(column);
            W.Until(asToken);
        }
        W.Until(asToken);
        W.NewLine(column);
        W.Through(asToken);
        if (body is not null) Statements(body, column);
    }

    /// <summary>Procedure / function parameters: the first one per "place first procedure parameter on new line", the rest one per line.</summary>
    private void Parameters(IList<ProcedureParameter> parameters, int column, int? parenContent)
    {
        var firstNew = S.Ddl.PlaceFirstProcedureParameterOnNewLine switch
        {
            "always" => "always",
            "never" => "never",
            _ => "ifMultipleItems",
        };
        int? typeColumn = null, defaultColumn = null;
        if (parenContent is null) W.Space();
        List(parameters, new ListLayout
        {
            KeywordColumn = column,
            StatementColumn = column,
            FirstOnNewLine = firstNew,
            Subsequent = S.Lists.PlaceSubsequentItemsOnNewLines,
            AlignToFirst = S.Lists.AlignSubsequentItemsWithFirstItem,
            Indent = true,
            AlignComments = S.Lists.AlignComments,
            OnLayout = (startOf, broken) =>
            {
                if (!broken || !S.Ddl.AlignDataTypesAndConstraints) return;
                int names = 0, types = 0;
                for (var i = 0; i < parameters.Count; i++)
                {
                    var start = startOf(i);
                    if (start < 0 || parameters[i].DataType is null) continue;
                    names = Math.Max(names, start + PhraseWidth(parameters[i].VariableName.FirstTokenIndex, parameters[i].VariableName.LastTokenIndex));
                    types = Math.Max(types, W.MeasureFlat(() => DataType(parameters[i].DataType, variable: false), parameters[i].DataType.FirstTokenIndex));
                }
                if (names == 0) return;
                typeColumn = names + 1;
                defaultColumn = typeColumn + types + 1;
            },
        }, (p, _) =>
        {
            W.Through(p.VariableName.LastTokenIndex);
            if (p.DataType is not null)
            {
                W.Until(p.DataType.FirstTokenIndex); // [AS]
                if (typeColumn is int tc) W.PadTo(tc);
                else W.Space();
                DataType(p.DataType, variable: false);
            }
            if (p.Value is not null)
            {
                var eq = LastSignificantBefore(p.Value.FirstTokenIndex);
                W.Until(eq);
                if (defaultColumn is int dc) W.PadTo(dc);
                else W.Space();
                W.Through(eq);
                W.Space();
                Scalar(p.Value);
            }
            if (p.LastTokenIndex >= W.NextToken) W.Space();
            W.Through(p.LastTokenIndex);
        });
    }

    private void Function(FunctionStatementBody f, int column)
    {
        W.Through(f.Name.LastTokenIndex);
        var open = NextSignificant(f.Name.LastTokenIndex + 1);
        if (open >= 0 && T[open].TokenType == TSqlTokenType.LeftParenthesis)
        {
            var close = MatchingParen(open);
            BeforeParen();
            if (f.Parameters.Count > 0) Parens(open, close, DdlParens(), c => Parameters(f.Parameters, column, c));
            else
            {
                W.Through(open);
                W.NoSpace();
                W.Through(close);
            }
        }

        var returns = FindWord(W.NextToken, f.LastTokenIndex, "RETURNS");
        if (returns >= 0)
        {
            W.Until(returns);
            W.NewLine(column);
            W.Through(returns);
            W.Space();
            if (f.ReturnType is TableValuedFunctionReturnType tv && tv.DeclareTableVariableBody?.Definition is { } definition)
            {
                var tableOpen = FindLast(returns, definition.FirstTokenIndex, TSqlTokenType.LeftParenthesis);
                if (tableOpen >= 0)
                {
                    W.Until(tableOpen);
                    BeforeParen();
                    TableDef(tableOpen, MatchingParen(tableOpen), definition, column);
                }
            }
        }

        var bodyFirst = f.StatementList?.Statements is { Count: > 0 } body ? body[0].FirstTokenIndex : f.LastTokenIndex;
        if (f.ReturnType is SelectFunctionReturnType inline)
        {
            // RETURNS TABLE [WITH …] AS RETURN [(] SELECT … [)]
            var asToken = FindLast(W.NextToken, inline.SelectStatement.FirstTokenIndex, TSqlTokenType.As);
            if (asToken >= 0)
            {
                W.Until(asToken);
                W.NewLine(column);
                W.Through(asToken);
            }
            var ret = FindLast(W.NextToken, inline.SelectStatement.FirstTokenIndex, TSqlTokenType.Return);
            if (ret >= 0)
            {
                W.Until(ret);
                W.NewLine(column);
                W.Through(ret);
            }
            var q = inline.SelectStatement.QueryExpression;
            if (q is QueryParenthesisExpression)
            {
                BeforeParen();
                Query(q, column);
            }
            else
            {
                W.NewLine(column);
                PrintSelect(inline.SelectStatement, column);
            }
            W.Through(f.LastTokenIndex);
            return;
        }

        var asBody = FindLast(W.NextToken, bodyFirst - 1, TSqlTokenType.As);
        if (asBody >= 0)
        {
            var options = NextSignificant(W.NextToken);
            if (options >= 0 && options < asBody && T[options].TokenType == TSqlTokenType.With)
            {
                W.NewLine(column);
                W.Until(asBody);
            }
            W.Until(asBody);
            W.NewLine(column);
            W.Through(asBody);
        }
        if (f.StatementList?.Statements is { } statements) Statements(statements, column);
    }

    private void View(ViewStatementBody v, int column)
    {
        W.Through(v.SchemaObjectName.LastTokenIndex);
        if (v.Columns.Count > 0)
        {
            var open = Find(v.SchemaObjectName.LastTokenIndex + 1, v.Columns[0].FirstTokenIndex, TSqlTokenType.LeftParenthesis);
            if (open >= 0)
            {
                W.Until(open);
                BeforeParen();
                ColumnListParens(open, MatchingParen(open), v.Columns);
            }
        }
        var asToken = FindLast(W.NextToken, v.SelectStatement.FirstTokenIndex, TSqlTokenType.As);
        if (asToken >= 0)
        {
            var options = NextSignificant(W.NextToken);
            if (options >= 0 && options < asToken)
            {
                W.NewLine(column);
                W.Until(asToken);
            }
            W.Until(asToken);
            W.NewLine(column);
            W.Through(asToken);
        }
        W.NewLine(column);
        PrintSelect(v.SelectStatement, column);
        var last = T[v.LastTokenIndex].TokenType == TSqlTokenType.Semicolon ? v.LastTokenIndex - 1 : v.LastTokenIndex;
        if (NextSignificant(W.NextToken) is var rest && rest >= 0 && rest <= last)
        {
            W.NewLine(column); // WITH CHECK OPTION
            W.Through(last);
        }
    }

    private void Trigger(TriggerStatementBody t, int column)
    {
        var body = t.StatementList?.Statements;
        var bodyFirst = body is { Count: > 0 } ? body[0].FirstTokenIndex : t.LastTokenIndex;
        var asToken = FindLast(t.FirstTokenIndex, bodyFirst - 1, TSqlTokenType.As);
        if (asToken < 0)
        {
            GenericStatement(t);
            return;
        }
        // CREATE TRIGGER name ON table / AFTER INSERT, UPDATE
        var on = Find(t.FirstTokenIndex, asToken, TSqlTokenType.On);
        if (on >= 0)
        {
            W.Until(on);
            W.NewLine(column);
        }
        W.Until(asToken);
        W.NewLine(column);
        W.Through(asToken);
        if (body is not null) Statements(body, column);
    }

    /// <summary>CREATE INDEX: one line when it fits; otherwise INCLUDE / WHERE / WITH / ON each start a line.</summary>
    private void CreateIndex(CreateIndexStatement c, int column)
    {
        var last = T[c.LastTokenIndex].TokenType == TSqlTokenType.Semicolon ? c.LastTokenIndex - 1 : c.LastTokenIndex;
        if (W.TryOneLine(() => W.Through(last))) return;

        var tableEnd = c.OnName.LastTokenIndex;
        W.Through(tableEnd);
        var depth = 0;
        for (var i = tableEnd + 1; i <= last; i++)
        {
            var t = T[i];
            if (t.TokenType == TSqlTokenType.LeftParenthesis) { depth++; continue; }
            if (t.TokenType == TSqlTokenType.RightParenthesis) { depth--; continue; }
            if (depth != 0) continue;
            if (IsWord(i, "INCLUDE") || t.TokenType is TSqlTokenType.Where or TSqlTokenType.With or TSqlTokenType.On)
            {
                W.Until(i);
                W.NewLine(column + (S.Ddl.IndentClauses ? Tab : 0));
            }
        }
        W.Through(last);
    }
}
