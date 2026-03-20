using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ErrorInfo
    {
        [Key(0)]
        public int Code { get; set; }

        [Key(1)]
        public string Message { get; set; } = string.Empty;
    }
}
