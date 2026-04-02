using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class QuickInfoResponse
    {
        [Key(0)]
        public string ObjectType { get; set; } = string.Empty;

        [Key(1)]
        public string Header { get; set; } = string.Empty;

        [Key(2)]
        public QuickInfoDetail[] Details { get; set; } = [];

        [Key(3)]
        public string? Description { get; set; }
    }

    [MessagePackObject]
    public class QuickInfoDetail
    {
        [Key(0)]
        public string Label { get; set; } = string.Empty;

        [Key(1)]
        public string Value { get; set; } = string.Empty;

        public QuickInfoDetail() { }
        public QuickInfoDetail(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }
}
