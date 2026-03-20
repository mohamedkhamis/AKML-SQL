using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class PlaceholderInfo
    {
        [Key(0)]
        public string VariableName { get; set; } = string.Empty;

        [Key(1)]
        public int Offset { get; set; }

        [Key(2)]
        public int Length { get; set; }

        [Key(3)]
        public string DefaultText { get; set; } = string.Empty;

        [Key(4)]
        public string? SchemaAwareType { get; set; }

        [Key(5)]
        public int GroupIndex { get; set; }
    }
}
