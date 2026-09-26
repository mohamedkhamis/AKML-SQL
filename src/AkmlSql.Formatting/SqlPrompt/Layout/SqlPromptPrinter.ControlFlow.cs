using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

internal sealed partial class SqlPromptPrinter
{
    // ── control flow ─────────────────────────────────────────────────────────

    private void If(IfStatement s, int column)
    {
        W.Through(s.FirstTokenIndex); // IF
        W.Space();
        Boolean(s.Predicate, new BoolContext(column, PhraseWidth(s.FirstTokenIndex, s.FirstTokenIndex), W.Column));
        Body(s.ThenStatement, column);

        if (s.ElseStatement is not { } otherwise) return;
        var elseToken = FindLast(s.ThenStatement.LastTokenIndex + 1, otherwise.FirstTokenIndex - 1, TSqlTokenType.Else);
        if (elseToken < 0) return;
        W.Until(elseToken);
        W.NewLine(column);
        W.Through(elseToken);
        if (otherwise is IfStatement)
        {
            // ELSE IF stays on one line
            W.Space();
            _blockColumn = column;
            Statement(otherwise);
        }
        else
        {
            Body(otherwise, column);
        }
    }

    private void While(WhileStatement s, int column)
    {
        W.Through(s.FirstTokenIndex); // WHILE
        W.Space();
        Boolean(s.Predicate, new BoolContext(column, PhraseWidth(s.FirstTokenIndex, s.FirstTokenIndex), W.Column));
        Body(s.Statement, column);
    }

    /// <summary>Column a nested block or ELSE IF belongs to, when it does not start its own line.</summary>
    private int? _blockColumn;

    /// <summary>
    /// The statement an IF / ELSE / WHILE controls: a BEGIN…END block (BEGIN on the next line or the
    /// same one, indented or not) or a single statement (indented when "indent contents" is on).
    /// </summary>
    private void Body(TSqlStatement body, int column)
    {
        var cf = S.ControlFlow;
        if (body is BeginEndBlockStatement)
        {
            if (cf.PlaceBeginAndEndOnNewLine)
            {
                var beginColumn = cf.IndentBeginAndEnd ? column + Tab : column;
                W.NewLine(beginColumn);
                _blockColumn = beginColumn;
            }
            else
            {
                W.Space();
                _blockColumn = cf.IndentBeginAndEnd ? column + Tab : column;
            }
            Statement(body);
            return;
        }
        W.NewLine(column + (cf.IndentContents ? Tab : 0));
        Statement(body);
    }

    /// <summary>BEGIN … END: contents indented when "indent contents" is on, END under BEGIN's column.</summary>
    private void BeginEnd(BeginEndBlockStatement b, int column)
    {
        W.Through(b.FirstTokenIndex); // BEGIN
        var end = FindLast(b.FirstTokenIndex, b.LastTokenIndex, TSqlTokenType.End);
        var content = column + (S.ControlFlow.IndentContents ? Tab : 0);
        Statements(b.StatementList.Statements, content);
        if (end < 0) return;
        W.Until(end);
        W.NewLine(column);
        W.Through(end);
    }

    /// <summary>BEGIN TRY … END TRY BEGIN CATCH … END CATCH.</summary>
    private void TryCatch(TryCatchStatement t, int column)
    {
        var content = column + (S.ControlFlow.IndentContents ? Tab : 0);
        var tryWord = NextSignificant(t.FirstTokenIndex + 1);
        W.Through(tryWord); // BEGIN TRY
        Statements(t.TryStatements.Statements, content);

        var afterTry = t.TryStatements.Statements.Count > 0 ? t.TryStatements.Statements[^1].LastTokenIndex + 1 : tryWord + 1;
        var catchWord = FindWord(afterTry, t.LastTokenIndex, "CATCH");
        if (catchWord < 0)
        {
            W.Through(t.LastTokenIndex);
            return;
        }
        var beginCatch = LastSignificantBefore(catchWord);
        var endTry = FindLast(afterTry, beginCatch - 1, TSqlTokenType.End);
        if (endTry >= 0)
        {
            W.Until(endTry);
            W.NewLine(column);
            W.Through(LastSignificantBefore(beginCatch)); // END TRY
        }
        W.Until(beginCatch);
        W.NewLine(column);
        W.Through(catchWord); // BEGIN CATCH
        Statements(t.CatchStatements.Statements, content);

        var endCatch = FindLast(W.NextToken, t.LastTokenIndex, TSqlTokenType.End);
        if (endCatch >= 0)
        {
            W.Until(endCatch);
            W.NewLine(column);
        }
        var last = T[t.LastTokenIndex].TokenType == TSqlTokenType.Semicolon ? t.LastTokenIndex - 1 : t.LastTokenIndex;
        W.Through(last); // END CATCH
    }

    // ── variables ────────────────────────────────────────────────────────────

    /// <summary>
    /// DECLARE @a type = value, …: a list (Lists options); with "align data types and values" the
    /// types and the = signs line up when the declarations are on separate lines.
    /// </summary>
    private void DeclareVariables(DeclareVariableStatement d, int column)
    {
        W.Through(d.FirstTokenIndex); // DECLARE
        var declarations = d.Declarations;
        int? typeColumn = null, valueColumn = null;
        W.Space();
        var firstColumn = TabStop(W.Column);

        List(declarations, ClauseList(column, column, firstColumn, onLayout: (startOf, broken) =>
        {
            if (!broken || !S.Variables.AlignDataTypesAndValues) return;
            var names = 0;
            var types = 0;
            for (var i = 0; i < declarations.Count; i++)
            {
                var start = startOf(i);
                if (start < 0) continue;
                var e = declarations[i];
                names = Math.Max(names, start + PhraseWidth(e.VariableName.FirstTokenIndex, e.VariableName.LastTokenIndex));
                if (e.DataType is not null)
                    types = Math.Max(types, W.MeasureFlat(() => DataType(e.DataType, variable: true), e.DataType.FirstTokenIndex));
            }
            typeColumn = names + 1;
            valueColumn = typeColumn + types + 1;
        }), (e, _) => DeclareElement(e, typeColumn, valueColumn, column));
    }

    private void DeclareElement(DeclareVariableElement e, int? typeColumn, int? valueColumn, int column)
    {
        W.Through(e.VariableName.LastTokenIndex);
        if (e.DataType is not null)
        {
            W.Until(e.DataType.FirstTokenIndex);  // [AS]
            if (typeColumn is int tc) W.PadTo(tc);
            else W.Space();
            DataType(e.DataType, variable: true);
        }
        if (e.Value is not null)
        {
            var eq = LastSignificantBefore(e.Value.FirstTokenIndex);
            W.Until(eq);
            if (valueColumn is int vc && e.DataType is not null) W.PadTo(vc);
            else W.Space();
            AssignedValue(eq, e.Value, column);
        }
        W.Through(e.LastTokenIndex);
    }

    /// <summary>DECLARE @t TABLE (…): the table definition in the DDL parenthesis style.</summary>
    private void DeclareTableVariable(DeclareTableVariableStatement d, int column)
    {
        var definition = d.Body?.Definition;
        if (definition is null)
        {
            GenericStatement(d);
            return;
        }
        var open = FindLast(d.FirstTokenIndex, definition.FirstTokenIndex, TSqlTokenType.LeftParenthesis);
        if (open < 0)
        {
            GenericStatement(d);
            return;
        }
        W.Until(open);
        BeforeParen();
        TableDef(open, MatchingParen(open), definition, column);
    }

    /// <summary>SET @x = value (and += …): the value moves to the next line when the statement is too long.</summary>
    private void SetVariable(SetVariableStatement s, int column)
    {
        if (s.Expression is null || s.Variable is null)
        {
            GenericStatement(s);
            return;
        }
        W.Through(s.FirstTokenIndex); // SET
        W.Space();
        W.Through(s.Variable.LastTokenIndex);
        var op = NextSignificant(s.Variable.LastTokenIndex + 1);
        if (op < 0 || !T[op].Text.EndsWith('=') || op >= s.Expression.FirstTokenIndex)
        {
            W.Through(s.LastTokenIndex);
            return;
        }
        W.Space();
        AssignedValue(op, s.Expression, column);
    }

    /// <summary>"= value" of a DECLARE or SET: on one line, or the value (with or without the =) on the next.</summary>
    private void AssignedValue(int equals, ScalarExpression value, int column)
    {
        var v = S.Variables;
        if (!W.IsFlat && v.PlaceAssignedValueOnNewLineIfTooLong)
        {
            var width = W.MeasureFlat(() => Scalar(value), value.FirstTokenIndex);
            if (W.Column + T[equals].Text.Length + 1 + width > Max)
            {
                if (v.PlaceEqualsSignOnNewLine)
                {
                    W.NewLine(column + Tab);
                    W.Through(equals);
                    W.Space();
                }
                else
                {
                    W.Through(equals);
                    W.NewLine(column + Tab);
                }
                Scalar(value);
                return;
            }
        }
        W.Through(equals);
        W.Space();
        Scalar(value);
    }

    /// <summary>A data type: nvarchar(100), decimal(10, 2). In DECLARE the "space between data type and precision" option applies.</summary>
    private void DataType(DataTypeReference t, bool variable)
    {
        var open = Find(t.FirstTokenIndex, t.LastTokenIndex, TSqlTokenType.LeftParenthesis);
        if (open < 0)
        {
            W.Through(t.LastTokenIndex);
            return;
        }
        W.Until(open);
        W.SpaceIf(variable && S.Variables.SpaceBetweenDataTypeAndPrecision);
        W.Through(open);
        W.NoSpace();
        W.Through(t.LastTokenIndex);
    }

    // ── EXEC ─────────────────────────────────────────────────────────────────

    /// <summary>EXEC procedure @a = 1, @b = 2: the parameters are a list.</summary>
    private void Execute(ExecuteStatement e, int column)
    {
        if (e.ExecuteSpecification?.ExecutableEntity is not ExecutableProcedureReference proc || proc.Parameters.Count == 0)
        {
            GenericStatement(e);
            return;
        }
        var parameters = proc.Parameters;
        W.Until(parameters[0].FirstTokenIndex); // EXEC [@r =] name
        W.Space();
        var firstColumn = W.Column;
        List(parameters, ClauseList(column, column, firstColumn), (p, _) =>
        {
            if (p.Variable is not null && p.ParameterValue is not null && p.Variable.LastTokenIndex < p.ParameterValue.FirstTokenIndex)
            {
                W.Through(p.Variable.LastTokenIndex);
                W.Space();
                W.Until(p.ParameterValue.FirstTokenIndex); // =
                W.Space();
                Scalar(p.ParameterValue);
            }
            else if (p.ParameterValue is not null)
            {
                Scalar(p.ParameterValue);
            }
            if (p.LastTokenIndex >= W.NextToken) W.Space();
            W.Through(p.LastTokenIndex);
        });
        var last = T[e.LastTokenIndex].TokenType == TSqlTokenType.Semicolon ? e.LastTokenIndex - 1 : e.LastTokenIndex;
        if (last >= W.NextToken)
        {
            W.Space();
            W.Through(last);
        }
    }
}
