#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// T099: Request to generate a "Script As" template for a database object.
    /// Sent Shell -> Engine as MessageType 67 (ScriptAs).
    /// </summary>
    [MessagePackObject]
    public class ScriptAsRequest
    {
        /// <summary>Session identifier for the active connection.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Schema name of the target object.</summary>
        [Key(1)]
        public string SchemaName { get; set; } = "dbo";

        /// <summary>Name of the target object.</summary>
        [Key(2)]
        public string ObjectName { get; set; } = string.Empty;

        /// <summary>
        /// Script template type. Supported values:
        /// "Create", "Insert", "Select", "Update", "Delete", "Merge", "Bcp".
        /// </summary>
        [Key(3)]
        public string TemplateType { get; set; } = "Select";
    }
}
