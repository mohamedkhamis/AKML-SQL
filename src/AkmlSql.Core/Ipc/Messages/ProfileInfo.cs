using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileInfo
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public string? Description { get; set; }

        [Key(2)]
        public string? Author { get; set; }

        [Key(3)]
        public bool IsBuiltIn { get; set; }

        [Key(4)]
        public bool IsActive { get; set; }

        [Key(5)]
        public string? BasedOn { get; set; }

        [Key(6)]
        public string? Modified { get; set; }
    }
}
