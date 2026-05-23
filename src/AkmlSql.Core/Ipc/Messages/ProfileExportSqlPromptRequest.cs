using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 020 T031 — Format Styles editor "Export to SQL Prompt" request.
    /// Asks the engine to load the named profile and write it as a
    /// <c>.sqlpromptstylev2</c> XML file via <c>SqlPromptExporter.ExportToFile</c>.
    /// Pairs with <see cref="ProfileExportSqlPromptResponse"/>.
    /// </summary>
    [MessagePackObject]
    public class ProfileExportSqlPromptRequest
    {
        /// <summary>Name of the profile to export (must exist in <c>ProfileManager.List()</c>).</summary>
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Absolute destination file path. Must be fully qualified and canonical (no traversal
        /// sequences like <c>..</c>). Conventionally ends with <c>.sqlpromptstylev2</c>, but the
        /// engine does not enforce the extension — the caller is responsible for the user-facing
        /// extension policy.
        /// </summary>
        [Key(1)]
        public string DestinationPath { get; set; } = string.Empty;
    }
}
