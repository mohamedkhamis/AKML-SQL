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
}
