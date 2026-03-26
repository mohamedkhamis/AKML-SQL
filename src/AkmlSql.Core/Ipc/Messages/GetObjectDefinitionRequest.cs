using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to retrieve the definition (source code or generated DDL) of a database object.
    /// Sent Shell -> Engine as MessageType 60 (GetObjectDefinition).
    /// </summary>
    [MessagePackObject]
    public class GetObjectDefinitionRequest
    {
        /// <summary>Session identifier to look up the active connection.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Name of the object to look up (e.g. "MyTable", "usp_GetUsers").</summary>
        [Key(1)]
        public string ObjectName { get; set; } = string.Empty;

        /// <summary>Optional schema qualifier (e.g. "dbo"). When null, all schemas are searched.</summary>
        [Key(2)]
        public string? SchemaName { get; set; }

        /// <summary>
        /// When <c>true</c>, the response is intended for inline peek display rather than a full new tab.
        /// The engine may return a shorter/summarized definition for peek mode.
        /// </summary>
        [Key(3)]
        public bool PeekOnly { get; set; }
    }
}
