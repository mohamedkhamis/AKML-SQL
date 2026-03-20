using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class SnippetImportRequest
    {
        [Key(0)]
        public string FileContent { get; set; } = string.Empty;

        [Key(1)]
        public int SourceFormat { get; set; } // 0=Auto, 1=SqlPromptXml, 2=SqlPromptJson, 3=SsmsNative, 4=AkmlSnippet

        [Key(2)]
        public string? NewSnippetName { get; set; }
    }

    [MessagePackObject]
    public class SnippetImportResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public int ImportedCount { get; set; }

        [Key(2)]
        public int FailedCount { get; set; }

        [Key(3)]
        public string[] FailedDetails { get; set; } = [];

        [Key(4)]
        public string[] SnippetIds { get; set; } = [];
    }
}
