using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileDeleteResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string? ErrorMessage { get; set; }
    }
}
