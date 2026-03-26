using Xunit;
using AkmlSql.Formatting.Layout;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Layout;

public class LayoutNodeTests
{
    // ── BreakType enum ────────────────────────────────────────────────────

    [Fact]
    public void BreakType_Values()
    {
        Assert.Equal(0, (int)BreakType.None);
        Assert.Equal(1, (int)BreakType.NewLine);
        Assert.Equal(2, (int)BreakType.EmptyLine);
    }

    // ── CommentAttachmentType enum ────────────────────────────────────────

    [Fact]
    public void CommentAttachmentType_Values()
    {
        Assert.Equal(0, (int)CommentAttachmentType.Trailing);
        Assert.Equal(1, (int)CommentAttachmentType.Leading);
        Assert.Equal(2, (int)CommentAttachmentType.Standalone);
    }

    // ── LayoutNode ────────────────────────────────────────────────────────

    [Fact]
    public void LayoutNode_Defaults()
    {
        var node = new LayoutNode();
        Assert.Equal(0, node.TokenIndex);
        Assert.Equal(TSqlTokenType.None, node.TokenType);
        Assert.Equal(string.Empty, node.OriginalText);
        Assert.Equal(string.Empty, node.FormattedText);
        Assert.Equal(0, node.IndentLevel);
        Assert.Equal(BreakType.None, node.PrecedingBreak);
        Assert.Equal(0, node.PrecedingSpaces);
        Assert.Null(node.TrailingComment);
        Assert.False(node.IsInNoformatRegion);
    }

    [Fact]
    public void LayoutNode_Mutations()
    {
        var comment = new CommentAttachment
        {
            TokenIndex = 5,
            Text = "-- inline",
            AttachmentType = CommentAttachmentType.Trailing
        };
        var node = new LayoutNode
        {
            TokenIndex = 10,
            TokenType = TSqlTokenType.Select,
            OriginalText = "select",
            FormattedText = "SELECT",
            IndentLevel = 2,
            PrecedingBreak = BreakType.NewLine,
            PrecedingSpaces = 4,
            TrailingComment = comment,
            IsInNoformatRegion = true
        };

        Assert.Equal(10, node.TokenIndex);
        Assert.Equal(TSqlTokenType.Select, node.TokenType);
        Assert.Equal("select", node.OriginalText);
        Assert.Equal("SELECT", node.FormattedText);
        Assert.Equal(2, node.IndentLevel);
        Assert.Equal(BreakType.NewLine, node.PrecedingBreak);
        Assert.Equal(4, node.PrecedingSpaces);
        Assert.NotNull(node.TrailingComment);
        Assert.Equal("-- inline", node.TrailingComment.Text);
        Assert.True(node.IsInNoformatRegion);
    }

    // ── CommentAttachment ─────────────────────────────────────────────────

    [Fact]
    public void CommentAttachment_Defaults()
    {
        var ca = new CommentAttachment();
        Assert.Equal(0, ca.TokenIndex);
        Assert.Equal(string.Empty, ca.Text);
        Assert.Equal(CommentAttachmentType.Trailing, ca.AttachmentType);
        Assert.Equal(0, ca.OriginalIndent);
    }

    [Fact]
    public void CommentAttachment_Mutations()
    {
        var ca = new CommentAttachment
        {
            TokenIndex = 7,
            Text = "/* block */",
            AttachmentType = CommentAttachmentType.Standalone,
            OriginalIndent = 4
        };
        Assert.Equal(7, ca.TokenIndex);
        Assert.Equal("/* block */", ca.Text);
        Assert.Equal(CommentAttachmentType.Standalone, ca.AttachmentType);
        Assert.Equal(4, ca.OriginalIndent);
    }
}
