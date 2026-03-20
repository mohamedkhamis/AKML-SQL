using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    public enum FormatActionType
    {
        CasingOnly = 0,
        InsertSemicolons = 1,
        RemoveSemicolons = 2,
        ExpandWildcards = 3,
        QualifyObjectNames = 4,
        AddSquareBrackets = 5,
        RemoveSquareBrackets = 6,
        AddAsKeyword = 7,
        RemoveAsKeyword = 8
    }

    [MessagePackObject]
    public class FormatActionRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string Text { get; set; } = string.Empty;

        [Key(2)]
        public int ActionType { get; set; }

        [Key(3)]
        public string? ProfileName { get; set; }
    }
}
