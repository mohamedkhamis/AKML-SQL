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
}
