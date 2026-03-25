#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// An actionable code suggestion from the AI chat panel.
    /// </summary>
    [MessagePackObject]
    public class CodeActionDto
    {
        /// <summary>Display label for the action button (e.g. "Apply to Editor").</summary>
        [Key(0)]
        public string Label { get; set; } = string.Empty;

        /// <summary>Action type: "applyToEditor", "copyToClipboard", or "runQuery".</summary>
        [Key(1)]
        public string ActionType { get; set; } = string.Empty;

        /// <summary>The SQL or code content associated with this action.</summary>
        [Key(2)]
        public string Code { get; set; } = string.Empty;
    }
}
