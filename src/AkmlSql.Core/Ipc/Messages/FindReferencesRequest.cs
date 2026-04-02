using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to find all objects that reference a given database object.
    /// Sent Shell -> Engine as MessageType 61 (FindReferences).
    /// </summary>
    [MessagePackObject]
    public class FindReferencesRequest
    {
        /// <summary>Session identifier to look up the active connection.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Name of the referenced object to search for.</summary>
        [Key(1)]
        public string ObjectName { get; set; } = string.Empty;

        /// <summary>Optional schema qualifier. When null, all schemas are searched.</summary>
        [Key(2)]
        public string? SchemaName { get; set; }
    }
}
