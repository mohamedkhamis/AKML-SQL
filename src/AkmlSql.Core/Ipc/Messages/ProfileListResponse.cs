using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileListResponse
    {
        [Key(0)]
        public ProfileInfo[] Profiles { get; set; } = [];
    }
}
