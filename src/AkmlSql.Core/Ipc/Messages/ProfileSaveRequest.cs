using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileSaveRequest
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public string ProfileJson { get; set; } = string.Empty;

        [Key(2)]
        public string? Description { get; set; }

        [Key(3)]
        public string? BasedOn { get; set; }
    }
}
