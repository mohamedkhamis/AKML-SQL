using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 020 T031 — response to <see cref="ProfileExportSqlPromptRequest"/>. Reports whether
    /// the export succeeded and how many SQL Prompt option entries the exporter wrote (from
    /// <c>SqlPromptExportResult.WrittenCount</c>). The shell surfaces the count in the Format
    /// Styles editor's "Export" confirmation.
    /// </summary>
    [MessagePackObject]
    public class ProfileExportSqlPromptResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Number of <c>&lt;Option Name= Value= /&gt;</c> elements the exporter wrote into the file.
        /// Zero on failure. Useful as a smoke-check that the profile actually carried mappable settings.
        /// </summary>
        [Key(2)]
        public int WrittenCount { get; set; }
    }
}
