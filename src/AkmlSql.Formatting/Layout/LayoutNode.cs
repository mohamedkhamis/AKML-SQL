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
}
