using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class RefreshRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;
    }
}
