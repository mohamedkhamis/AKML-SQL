using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class RefactorApplyRequest
    {
        [Key(0)] public string              SessionId           { get; set; } = string.Empty;
        [Key(1)] public int                 RequestId           { get; set; }
        [Key(2)] public int                 OperationType       { get; set; }
        [Key(3)] public RefactorChangeInfo[] ApprovedChanges    { get; set; } = [];
        [Key(4)] public bool                CreateBackups       { get; set; } = true;
        [Key(5)] public bool                FormatAfterRefactor { get; set; } = true;
        [Key(6)] public string              SessionProfileName  { get; set; } = string.Empty;

        /// <summary>
        /// The original document text the <see cref="ApprovedChanges"/> offsets refer to. The engine's
        /// heavyweight <c>ApplyAsync</c> reconstructs the result by applying the changes onto this text
        /// (offsets are absolute into it). Required whenever a consumer reads
        /// <c>RefactorApplyResponse.UpdatedDocumentText</c> (the web edition does): if left empty, every
        /// change at offset &gt; 0 is skipped and the returned document is truncated/empty. Callers that
        /// apply changes to their own buffer (the shell's ITextBuffer path) may leave it empty.
        /// </summary>
        [Key(7)] public string              DocumentText        { get; set; } = string.Empty;
    }
}
