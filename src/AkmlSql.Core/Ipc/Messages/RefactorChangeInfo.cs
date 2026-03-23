using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class RefactorChangeInfo
    {
        /// <summary>Absolute file path; empty string = current editor document.</summary>
        [Key(0)] public string FilePath { get; set; } = string.Empty;

        /// <summary>Character offset in the file where replacement begins.</summary>
        [Key(1)] public int StartOffset { get; set; }

        /// <summary>Character offset where replacement ends (exclusive).</summary>
        [Key(2)] public int EndOffset { get; set; }

        /// <summary>Original text being replaced (for diff display).</summary>
        [Key(3)] public string OldText { get; set; } = string.Empty;

        /// <summary>Replacement text.</summary>
        [Key(4)] public string NewText { get; set; } = string.Empty;

        /// <summary>1-based line number (display only).</summary>
        [Key(5)] public int Line { get; set; }

        /// <summary>1-based column number (display only).</summary>
        [Key(6)] public int Column { get; set; }

        /// <summary>±2 surrounding lines for diff view.</summary>
        [Key(7)] public string ContextSnippet { get; set; } = string.Empty;

        /// <summary>Grouping hint: "rename" | "structure" | "wrap" | "declaration".</summary>
        [Key(8)] public string ChangeCategory { get; set; } = "rename";
    }
}
