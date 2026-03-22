using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Stage 3: Walks the token stream and produces LayoutNode decisions for each semantic token.
/// Uses a clause-tracking approach with profile-driven line break and indentation rules.
/// </summary>
public class LayoutEngine
{
    public List<LayoutNode> BuildLayout(
        TSqlScript script,
        IList<TSqlParserToken> tokens,
        List<CommentAttachment> comments,
        FormattingProfile profile,
        List<NoformatRegion>? noformatRegions = null)
    {
        var nodes = new List<LayoutNode>();
        var commentLookup = new Dictionary<int, CommentAttachment>();
        foreach (var c in comments)
            commentLookup[c.TokenIndex] = c;

        var decider = new LineBreakDecider(profile);
        var tracker = new ClauseTracker();
        var indentTracker = new IndentationTracker();

        bool isFirstSemanticToken = true;
        int statementIndex = 0;
        var statementStarts = BuildStatementStartSet(script);
        TSqlTokenType? prevSemanticTokenType = null;
        string? prevSemanticTokenText = null;
        int baseIndent = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.TokenType == TSqlTokenType.EndOfFile)
                break;
            if (t.TokenType == TSqlTokenType.WhiteSpace)
                continue;

            // --- Handle comments ---
            if (t.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            {
                if (commentLookup.TryGetValue(i, out var attachment))
                {
                    if (attachment.AttachmentType == CommentAttachmentType.Trailing)
                    {
                        // Attach to previous node as trailing comment
                        if (nodes.Count > 0)
                            nodes[^1].TrailingComment = attachment;
                        continue;
                    }

                    // Leading/Standalone comments become their own layout nodes
                    var commentNode = new LayoutNode
                    {
                        TokenIndex = i,
                        TokenType = t.TokenType,
                        OriginalText = t.Text,
                        FormattedText = t.Text,
                        IndentLevel = baseIndent + indentTracker.CurrentDepth,
                        PrecedingBreak = isFirstSemanticToken ? BreakType.None :
                            (attachment.AttachmentType == CommentAttachmentType.Standalone
                                ? BreakType.EmptyLine
                                : BreakType.NewLine),
                        PrecedingSpaces = 0
                    };
                    nodes.Add(commentNode);
                    continue;
                }

                // Comment not in lookup — treat as standalone node
                nodes.Add(new LayoutNode
                {
                    TokenIndex = i,
                    TokenType = t.TokenType,
                    OriginalText = t.Text,
                    FormattedText = t.Text,
                    IndentLevel = baseIndent + indentTracker.CurrentDepth,
                    PrecedingBreak = isFirstSemanticToken ? BreakType.None : BreakType.NewLine,
                    PrecedingSpaces = 0
                });
                continue;
            }

            // --- Determine if this is a new statement ---
            bool isFirstInStatement = false;
            if (statementStarts.Contains(t.Offset) && !isFirstSemanticToken)
            {
                isFirstInStatement = true;
                statementIndex++;
                tracker.Reset();
                baseIndent = 0;
                indentTracker.Reset();
            }

            // --- Handle parentheses indentation ---
            int parenIndent = indentTracker.ProcessToken(t.TokenType, t.Text);

            // --- Get clause context and decide formatting ---
            var clauseContext = tracker.CurrentClause;

            var decision = decider.Decide(
                t.TokenType,
                t.Text,
                clauseContext,
                isFirstSemanticToken,
                isFirstInStatement,
                prevSemanticTokenType,
                prevSemanticTokenText);

            // Compute final indent level
            int indentLevel;
            if (decision.Break is BreakType.NewLine or BreakType.EmptyLine)
            {
                indentLevel = baseIndent + parenIndent + decision.IndentDelta;
            }
            else
            {
                indentLevel = 0; // No break = no indentation needed
            }

            // Special handling for closing paren — no indent delta
            if (t.TokenType == TSqlTokenType.RightParenthesis)
            {
                if (decision.Break == BreakType.None && prevSemanticTokenType != TSqlTokenType.LeftParenthesis)
                {
                    // Put closing paren on new line at the paren's open level
                    decision = new BreakDecision(BreakType.NewLine, 0, 0);
                    indentLevel = baseIndent + parenIndent;
                }
                else
                {
                    indentLevel = baseIndent + parenIndent;
                }
            }

            // Special handling for open paren — no break, no space in most cases
            if (t.TokenType == TSqlTokenType.LeftParenthesis)
            {
                if (profile.Whitespace.SpaceBeforeParentheses)
                    decision = new BreakDecision(BreakType.None, 0, 1);
                else if (prevSemanticTokenType != null && !IsOpenParenNoSpace(prevSemanticTokenType.Value))
                    decision = new BreakDecision(BreakType.None, 0, 1);
                else
                    decision = new BreakDecision(BreakType.None, 0, 0);
            }

            // Operator spacing
            if (IsOperator(t.TokenType) && profile.Whitespace.SpaceAroundOperators)
            {
                decision = new BreakDecision(BreakType.None, 0, 1);
            }

            // Star after open paren with no space
            if (t.TokenType == TSqlTokenType.Star && prevSemanticTokenType == TSqlTokenType.LeftParenthesis)
                decision = new BreakDecision(BreakType.None, 0, 0);

            // Token after operator gets a space
            if (prevSemanticTokenType.HasValue && IsOperator(prevSemanticTokenType.Value)
                                               && profile.Whitespace.SpaceAroundOperators
                                               && decision is { Break: BreakType.None, PrecedingSpaces: 0 })
            {
                decision = decision with { PrecedingSpaces = 1 };
            }

            // Dot (period) — no space around
            if (t.TokenType == TSqlTokenType.Dot)
                decision = new BreakDecision(BreakType.None, 0, 0);
            if (prevSemanticTokenType == TSqlTokenType.Dot)
                decision = new BreakDecision(BreakType.None, 0, 0);

            var node = new LayoutNode
            {
                TokenIndex = i,
                TokenType = t.TokenType,
                OriginalText = t.Text,
                FormattedText = t.Text,
                IndentLevel = Math.Clamp(indentLevel, 0, 50),
                PrecedingBreak = decision.Break,
                PrecedingSpaces = decision.PrecedingSpaces,
                IsInNoformatRegion = noformatRegions != null && NoformatScanner.IsInNoformatRegion(noformatRegions, t.Offset)
            };

            nodes.Add(node);

            // --- Update clause tracker ---
            tracker.Update(t.TokenType, t.Text);

            prevSemanticTokenType = t.TokenType;
            prevSemanticTokenText = t.Text;
            isFirstSemanticToken = false;
        }

        return nodes;
    }

    /// <summary>
    /// Builds a set of token offsets that correspond to the start of each statement in the script.
    /// Used to detect statement boundaries for empty-line insertion.
    /// </summary>
    private static HashSet<int> BuildStatementStartSet(TSqlScript script)
    {
        var starts = new HashSet<int>();
        foreach (var batch in script.Batches)
        {
            for (int s = 1; s < batch.Statements.Count; s++)
            {
                starts.Add(batch.Statements[s].StartOffset);
            }
        }
        return starts;
    }

    private static bool IsOpenParenNoSpace(TSqlTokenType prevType)
    {
        // Function calls and keywords like IN(...) don't need space before (
        return prevType switch
        {
            TSqlTokenType.Identifier or TSqlTokenType.Coalesce or TSqlTokenType.NullIf or
            TSqlTokenType.Convert or
            TSqlTokenType.In or TSqlTokenType.Exists => true,
            _ => false,
        };
    }

    private static bool IsOperator(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.EqualsSign or TSqlTokenType.GreaterThan or TSqlTokenType.LessThan or
            TSqlTokenType.Plus or TSqlTokenType.Minus or TSqlTokenType.Star or
            TSqlTokenType.Divide or TSqlTokenType.PercentSign or
            TSqlTokenType.Bang => true,
            _ => false,
        };
    }
}

/// <summary>
/// Tracks the current SQL clause context as tokens are processed sequentially.
/// </summary>
internal class ClauseTracker
{
    private ClauseContext _current = ClauseContext.None;
    private bool _selectSawKeyword;
    private bool _selectSawFirstItem;
    private int _parenDepth;

    public ClauseContext CurrentClause => _current;

    public void Reset()
    {
        _current = ClauseContext.None;
        _selectSawKeyword = false;
        _selectSawFirstItem = false;
        _parenDepth = 0;
    }

    public void Update(TSqlTokenType tokenType, string text)
    {
        var upper = text.ToUpperInvariant();

        // Track parentheses depth — don't change clause inside parens
        if (tokenType == TSqlTokenType.LeftParenthesis)
        {
            _parenDepth++;
            return;
        }
        if (tokenType == TSqlTokenType.RightParenthesis)
        {
            _parenDepth = Math.Max(0, _parenDepth - 1);
            return;
        }

        // Don't track clause changes inside parentheses (subqueries, function args, etc.)
        if (_parenDepth > 0)
            return;

        switch (tokenType)
        {
            case TSqlTokenType.Select:
                _current = ClauseContext.Select;
                _selectSawKeyword = true;
                _selectSawFirstItem = false;
                break;

            case TSqlTokenType.Top when _current == ClauseContext.Select:
            case TSqlTokenType.Distinct when _current == ClauseContext.Select:
                // Still in SELECT clause header, pending first item
                break;

            case TSqlTokenType.From:
                _current = ClauseContext.From;
                _selectSawKeyword = false;
                break;

            case TSqlTokenType.Where:
                _current = ClauseContext.Where;
                break;

            case TSqlTokenType.Having:
                _current = ClauseContext.Having;
                break;

            case TSqlTokenType.Join:
                _current = ClauseContext.Join;
                break;

            case TSqlTokenType.On when _current == ClauseContext.Join:
                _current = ClauseContext.JoinOn;
                break;

            case TSqlTokenType.Insert:
                _current = ClauseContext.Insert;
                break;

            case TSqlTokenType.Update:
                _current = ClauseContext.Update;
                break;

            case TSqlTokenType.Delete:
                _current = ClauseContext.Delete;
                break;

            case TSqlTokenType.Set when _current == ClauseContext.Update:
                _current = ClauseContext.Set;
                break;

            case TSqlTokenType.Values:
                _current = ClauseContext.Values;
                break;

            case TSqlTokenType.With:
                _current = ClauseContext.With;
                break;

            case TSqlTokenType.Semicolon:
                _current = ClauseContext.None;
                _selectSawKeyword = false;
                _selectSawFirstItem = false;
                break;

            default:
                // Handle GROUP BY and ORDER BY
                if (upper == "GROUP")
                {
                    _current = ClauseContext.GroupBy;
                    break;
                }
                if (upper == "ORDER")
                {
                    _current = ClauseContext.OrderBy;
                    break;
                }

                // Track first item after SELECT
                if (_selectSawKeyword && _current == ClauseContext.Select && !_selectSawFirstItem)
                {
                    // This is the first actual item after SELECT [DISTINCT] [TOP N]
                    if (tokenType != TSqlTokenType.Integer) // Skip TOP N value
                    {
                        _selectSawFirstItem = true;
                        _current = ClauseContext.Select;
                    }
                }

                // After a JOIN modifier (INNER, LEFT, etc.), stay in FROM context
                // so the decider can see the JOIN keyword coming
                if (_current == ClauseContext.From &&
                    tokenType is TSqlTokenType.Inner or TSqlTokenType.Left or TSqlTokenType.Right or TSqlTokenType.Full or TSqlTokenType.Cross)
                {
                    // Keep From context so JOIN will be recognized
                }
                break;
        }

        // Transition SelectPendingFirstItem: after we see SELECT (and optional TOP/DISTINCT),
        // the next non-keyword token triggers this
        if (_current == ClauseContext.Select && _selectSawKeyword && !_selectSawFirstItem)
        {
            // Check if this is a qualifier token (TOP value, etc.)
            // The LineBreakDecider checks for SelectPendingFirstItem
            _current = ClauseContext.SelectPendingFirstItem;
        }
    }
}
