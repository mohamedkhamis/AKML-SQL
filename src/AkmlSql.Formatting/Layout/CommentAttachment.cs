namespace AkmlSql.Formatting.Layout;

public enum CommentAttachmentType
{
    Trailing,
    Leading,
    Standalone
}

public class CommentAttachment
{
    public int TokenIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public CommentAttachmentType AttachmentType { get; set; }
    public int OriginalIndent { get; set; }
}
