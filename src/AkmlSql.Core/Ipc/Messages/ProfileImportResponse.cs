using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileImportResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public int MappedOptionsCount { get; set; }

        [Key(2)]
        public int UnmappedOptionsCount { get; set; }

        [Key(3)]
        public string[]? UnmappedOptions { get; set; }

        [Key(4)]
        public string? ErrorMessage { get; set; }
    }
}
