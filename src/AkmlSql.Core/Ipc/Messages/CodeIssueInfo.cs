using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class CodeIssueInfo
    {
        [Key(0)] public string RuleId { get; set; } = string.Empty;
        [Key(1)] public int Severity { get; set; }   // 0=Hint,1=Info,2=Warning,3=Error
        [Key(2)] public string Message { get; set; } = string.Empty;
        [Key(3)] public int StartOffset { get; set; }
        [Key(4)] public int EndOffset { get; set; }
        [Key(5)] public int Line { get; set; }
        [Key(6)] public int Column { get; set; }
        [Key(7)] public FixActionInfo[] FixActions { get; set; } = [];

        /// <summary>
        /// Spec 030 T055 (FR-028) — the offending rule's one-line description (from the engine's
        /// RuleMetadataCatalog). Carried per-issue so the shell's Ctrl-hover issue-details popup can
        /// show it without referencing the engine-only catalog. Empty when the rule has no catalog entry.
        /// </summary>
        [Key(8)] public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Spec 030 T055 (FR-028) — an optional reference/documentation URL for the rule (http/https).
        /// Rendered as a clickable link in the issue-details popup. Empty when none is configured.
        /// </summary>
        [Key(9)] public string ReferenceUrl { get; set; } = string.Empty;
    }
}
