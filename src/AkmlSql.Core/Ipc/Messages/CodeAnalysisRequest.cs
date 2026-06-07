using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class CodeAnalysisRequest
    {
        [Key(0)] public string SessionId { get; set; } = string.Empty;
        [Key(1)] public string RequestId { get; set; } = string.Empty;
        [Key(2)] public string DocumentText { get; set; } = string.Empty;
        [Key(3)] public int DocumentVersion { get; set; }

        /// <summary>
        /// Spec 030 (T049): absolute path of the document being analysed. The engine derives its
        /// directory to locate the nearest <c>.casettings</c> so per-project rule config + suppressions
        /// apply in the live editor (matching the CLI). Null/empty (unsaved buffer) ⇒ global defaults.
        /// </summary>
        [Key(4)] public string? FilePath { get; set; }
    }
}
