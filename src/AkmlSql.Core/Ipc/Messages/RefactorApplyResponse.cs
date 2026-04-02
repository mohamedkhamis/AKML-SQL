using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class RefactorApplyResponse
    {
        /// <summary>True if all approved changes applied successfully.</summary>
        [Key(0)] public bool     Success             { get; set; }

        /// <summary>Number of changes successfully applied.</summary>
        [Key(1)] public int      AppliedCount        { get; set; }

        /// <summary>Files that could not be written (read-only, locked, or stale).</summary>
        [Key(2)] public string[] FailedFilePaths     { get; set; } = [];

        /// <summary>Paths of created backup files.</summary>
        [Key(3)] public string[] BackupFilePaths     { get; set; } = [];

        /// <summary>New text for the current editor document.</summary>
        [Key(4)] public string   UpdatedDocumentText { get; set; } = string.Empty;
    }
}
