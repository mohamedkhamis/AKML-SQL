using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class SnippetSaveRequest
    {
        [Key(0)]
        public string SnippetJson { get; set; } = string.Empty;

        [Key(1)]
        public bool IsNew { get; set; }
    }

    [MessagePackObject]
    public class SnippetSaveResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string? ErrorMessage { get; set; }
    }

    [MessagePackObject]
    public class SnippetDeleteRequest
    {
        [Key(0)]
        public string SnippetId { get; set; } = string.Empty;
    }

    [MessagePackObject]
    public class SnippetDeleteResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string? ErrorMessage { get; set; }
    }
}
