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
    }
}
