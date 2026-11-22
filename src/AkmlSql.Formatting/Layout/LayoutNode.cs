using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

public enum BreakType
{
    None,
    NewLine,
    EmptyLine
}

public class LayoutNode
{
    public int TokenIndex { get; set; }
    public TSqlTokenType TokenType { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public string FormattedText { get; set; } = string.Empty;
    public int IndentLevel { get; set; }
    public BreakType PrecedingBreak { get; set; }

    /// <summary>
    /// How many empty lines a <see cref="BreakType.EmptyLine"/> break stands for. One everywhere except between
    /// statements, where the style's emptyLineBetweenStatements count applies (the style editor, its preview and the
    /// PostgreSQL layout honour the number; the T-SQL layout read it as a yes/no and always wrote one — UI scan
    /// 2026-11-03, W6).
    /// </summary>
    public int EmptyLineCount { get; set; } = 1;

    /// <summary>
    /// Set on the first of the comments written directly above a statement: it carries the statement's empty lines
    /// (<see cref="EmptyLineCount"/>) in the statement's place, so the comments stay with the statement they introduce
    /// (review of W6). A structural empty line for the whitespace rules, like the statement's own would be.
    /// </summary>
    public bool LeadsStatement { get; set; }
    public int PrecedingSpaces { get; set; }
    public CommentAttachment? TrailingComment { get; set; }
    public bool IsInNoformatRegion { get; set; }

    /// <summary>
    /// Opt-in absolute leading-space count for a line-start token, overriding the
    /// <see cref="IndentLevel"/>×tabSize indent. Default -1 = unset (use IndentLevel). Set only by
    /// the right-align finalization pass (<c>RightAligner</c>), which needs per-space columns the
    /// tab grid can't hit. Honored by <c>TextEmitter</c> in spaces mode only (tabs can't sub-align).
    /// </summary>
    public int AbsoluteLeadingSpaces { get; set; } = -1;

    /// <summary>
    /// Set on a token kept exactly as written (text the parser could not read, see
    /// <c>Pipeline.UnparsedText</c>): the whitespace the emitter writes in front of it, in place of
    /// <see cref="PrecedingBreak"/>, <see cref="PrecedingSpaces"/> and any indentation. The run's
    /// first token carries the line breaks that start it; the others carry their original
    /// whitespace. Null for every other node.
    /// </summary>
    public string? LeadingWhitespace { get; set; }

    /// <summary>
    /// Spec 047 (FMT-002): the token starts a statement of the parse tree, at any depth (a block's statements, an
    /// IF / ELSE / WHILE body, a batch's). Rules that measure "the rest of this statement" stop here, so a short
    /// statement is never collapsed onto the statement after it ("END WHILE @a > 0").
    /// </summary>
    public bool StartsStatement { get; set; }

    /// <summary>
    /// Spec 047 (FMT-002): on a statement's first token, the token index of that statement's last token (the outermost
    /// statement starting there, its body included); -1 elsewhere. A rule that measures a statement stops after it.
    /// </summary>
    public int StatementLastTokenIndex { get; set; } = -1;

    /// <summary>
    /// Spec 047 (FMT-004): for a block comment, the column its <c>/*</c> had in the input (tabs counted at the style's tab
    /// size), so its other lines can move by exactly as much as the opener; -1 when unknown.
    /// </summary>
    public int OriginalColumn { get; set; } = -1;

    /// <summary>
    /// Spec 047 (FMT-002): the token index of the keyword that owns this token, from the parse tree, or -1. Set on the
    /// first token of the BEGIN…END or TRY…CATCH body of an IF, ELSE or WHILE (owner: that IF, ELSE or WHILE) and on an
    /// ELSE (owner: its IF). A block keyword lines up with its owner's line, and an ELSE with the line of its own IF.
    /// </summary>
    public int OwnerTokenIndex { get; set; } = -1;
}
