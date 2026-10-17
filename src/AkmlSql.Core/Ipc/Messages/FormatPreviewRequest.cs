using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class FormatPreviewRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string SampleText { get; set; } = string.Empty;

        [Key(2)]
        public string ProfileJson { get; set; } = string.Empty;

        /// <summary>
        /// Spec 042 — the <c>DatabaseKind</c> the sample is written in: 0 (the default) SQL Server, 1 PostgreSQL. The Format Styles page
        /// has no session to ask, so the sample's language is stated. Older peers ignore it and format the sample as T-SQL.
        /// </summary>
        [Key(3)]
        public int Dialect { get; set; }
    }
}
