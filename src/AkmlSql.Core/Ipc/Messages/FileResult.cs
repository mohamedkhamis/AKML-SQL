using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FileResult
    {
        /// <summary>Status values: 0=Success, 1=Failed, 2=Skipped, 3=Unchanged</summary>
        [Key(0)]
        public string FilePath { get; set; } = string.Empty;

        [Key(1)]
        public int Status { get; set; }

        [Key(2)]
        public int LinesChanged { get; set; }

        [Key(3)]
        public string? ErrorMessage { get; set; }
    }
}
