using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing the definition of a database object.
    /// Sent Engine -> Shell as MessageType 160 (GetObjectDefinitionResult).
    /// </summary>
    [MessagePackObject]
    public class GetObjectDefinitionResponse
    {
        /// <summary>Whether the object was found and its definition was retrieved.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>The SQL definition text (CREATE statement or generated DDL).</summary>
        [Key(1)]
        public string? Definition { get; set; }

        /// <summary>Object type description (e.g. "Procedure", "Table", "View").</summary>
        [Key(2)]
        public string? ObjectType { get; set; }

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(3)]
        public string? Error { get; set; }

        /// <summary>Fully qualified object name (schema.name) for display purposes.</summary>
        [Key(4)]
        public string? FullName { get; set; }
    }
}
