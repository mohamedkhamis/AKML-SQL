using MessagePack;

namespace AkmlSql.Core.Ipc.Messages;

[MessagePackObject]
public class WildcardExpansionRequest
{
    /// <summary>Session ID for schema cache lookup.</summary>
    [Key(0)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Cursor position in the document (at or near the *).</summary>
    [Key(1)]
    public int CursorOffset { get; set; }

    /// <summary>Full document text (sent directly to avoid session sync timing issues).</summary>
    [Key(2)]
    public string DocumentText { get; set; } = string.Empty;

    /// <summary>
    /// Qualifier before the wildcard. null for bare *, "o" for o.*.
    /// </summary>
    [Key(3)]
    public string? Qualifier { get; set; }
}
