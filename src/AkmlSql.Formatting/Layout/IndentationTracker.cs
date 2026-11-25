using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// Tracks current indentation depth based on parentheses, BEGIN/END blocks, subqueries,
/// and clause nesting in SQL statements.
/// </summary>
public class IndentationTracker
{
    private int _depth;
    private readonly Stack<IndentContext> _contextStack = new();

    public int CurrentDepth => _depth;

    /// <summary>
    /// Increases indent depth by 1 and pushes a context reason.
    /// </summary>
    public void Push(IndentReason reason)
    {
        _contextStack.Push(new IndentContext(reason, _depth));
        _depth++;
    }

    /// <summary>
    /// Decreases indent depth by 1, popping the most recent context.
    /// </summary>
    public void Pop()
    {
        if (_contextStack.Count > 0)
        {
            _contextStack.Pop();
            _depth = _contextStack.Count > 0 ? _contextStack.Peek().BaseDepth + 1 : 0;
        }
        else
        {
            _depth = Math.Max(0, _depth - 1);
        }
    }

    /// <summary>
    /// Peeks at the current context reason.
    /// </summary>
    public IndentReason? CurrentReason =>
        _contextStack.Count > 0 ? _contextStack.Peek().Reason : null;

    /// <summary>
    /// Resets all indentation state.
    /// </summary>
    public void Reset()
    {
        _depth = 0;
        _contextStack.Clear();
        _openCases = 0;
    }

    /// <summary>
    /// Sets the depth directly (used when switching between statement contexts).
    /// </summary>
    public void SetDepth(int depth)
    {
        _depth = Math.Max(0, depth);
    }

    /// <summary>
    /// Processes a token and updates indentation accordingly.
    /// Returns the indent level to use for this token (before any push/pop for the token itself).
    /// </summary>
    public int ProcessToken(TSqlTokenType tokenType, string text, string? nextText = null)
    {
        var upperText = text.ToUpperInvariant();
        var nextUpper = nextText?.ToUpperInvariant();

        // Tokens that decrease indent BEFORE the token is emitted
        switch (tokenType)
        {
            case TSqlTokenType.RightParenthesis:
                Pop();
                return _depth;

            // Spec 047 (FMT-002): END also closes a CASE expression, and END CONVERSATION is a statement; neither closes
            // a block. Popping for them took a level from every later line of the block.
            case TSqlTokenType.End when upperText == "END" && _openCases > 0:
                _openCases--;
                return _depth;

            case TSqlTokenType.End when upperText == "END" && nextUpper != "CONVERSATION":
                Pop();
                return _depth;

            case TSqlTokenType.Case:
                _openCases++;
                return _depth;
        }

        int currentIndent = _depth;

        // Tokens that increase indent AFTER the token is emitted
        switch (tokenType)
        {
            case TSqlTokenType.LeftParenthesis:
                Push(IndentReason.Parenthesis);
                break;

            // BEGIN TRAN[SACTION], BEGIN DISTRIBUTED TRAN, BEGIN DIALOG and BEGIN CONVERSATION TIMER start statements and
            // open no block (spec 047, FMT-002).
            case TSqlTokenType.Begin when upperText == "BEGIN" && !StartsStatementNotBlock(nextUpper):
                Push(IndentReason.BeginEnd);
                break;
        }

        return currentIndent;
    }

    private int _openCases;

    private static bool StartsStatementNotBlock(string? nextUpper) =>
        nextUpper is "TRAN" or "TRANSACTION" or "DISTRIBUTED" or "DIALOG" or "CONVERSATION";
}

public enum IndentReason
{
    Clause,
    Parenthesis,
    BeginEnd,
    Subquery,
    CaseExpression
}

internal record struct IndentContext(IndentReason Reason, int BaseDepth);
