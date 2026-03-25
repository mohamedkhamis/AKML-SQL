#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing all objects that reference a given database object.
    /// Sent Engine -> Shell as MessageType 161 (FindReferencesResult).
    /// </summary>
    [MessagePackObject]
    public class FindReferencesResponse
    {
        /// <summary>Whether the lookup succeeded.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>List of referencing objects.</summary>
        [Key(1)]
        public ObjectReferenceDto[] References { get; set; } = [];

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(2)]
        public string? Error { get; set; }
    }

    /// <summary>
    /// A single reference to a database object from another object.
    /// </summary>
    [MessagePackObject]
    public class ObjectReferenceDto
    {
        /// <summary>Schema of the referencing object.</summary>
        [Key(0)]
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>Name of the referencing object.</summary>
        [Key(1)]
        public string ObjectName { get; set; } = string.Empty;

        /// <summary>Type of the referencing object (e.g. "Procedure", "View", "Function").</summary>
        [Key(2)]
        public string ObjectType { get; set; } = string.Empty;

        /// <summary>Optional line number within the referencing object's definition.</summary>
        [Key(3)]
        public int? ReferenceLine { get; set; }
    }
}
