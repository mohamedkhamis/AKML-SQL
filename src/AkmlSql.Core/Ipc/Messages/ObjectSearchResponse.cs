#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response containing database objects matching a search pattern.
    /// Sent Engine -> Shell as MessageType 162 (ObjectSearchResult).
    /// </summary>
    [MessagePackObject]
    public class ObjectSearchResponse
    {
        /// <summary>Whether the search succeeded.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Matching database objects.</summary>
        [Key(1)]
        public ObjectSearchResultDto[] Results { get; set; } = [];

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(2)]
        public string? Error { get; set; }
    }

    /// <summary>
    /// A single database object search result.
    /// </summary>
    [MessagePackObject]
    public class ObjectSearchResultDto
    {
        /// <summary>Schema name (e.g. "dbo").</summary>
        [Key(0)]
        public string SchemaName { get; set; } = string.Empty;

        /// <summary>Object name (e.g. "usp_GetUsers").</summary>
        [Key(1)]
        public string ObjectName { get; set; } = string.Empty;

        /// <summary>Object type (e.g. "Table", "Procedure", "View").</summary>
        [Key(2)]
        public string ObjectType { get; set; } = string.Empty;
    }
}
