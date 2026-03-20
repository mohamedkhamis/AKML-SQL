using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileDeleteRequest
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;
    }
}
