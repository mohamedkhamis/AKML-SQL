using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class RefreshResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public int ObjectCount { get; set; }

        [Key(2)]
        public string? ErrorMessage { get; set; }
    }
}
