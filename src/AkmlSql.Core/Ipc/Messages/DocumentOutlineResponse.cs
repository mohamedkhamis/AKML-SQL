using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response from the engine with the document outline tree.
    /// Sent Engine -> Shell as MessageType 164 (DocumentOutlineResult).
    /// </summary>
    [MessagePackObject]
    public class DocumentOutlineResponse
    {
        /// <summary>Whether the request was processed successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Top-level nodes of the outline tree.</summary>
        [Key(1)]
        public OutlineNodeDto[] RootNodes { get; set; } = [];

        /// <summary>Error message if the request failed.</summary>
        [Key(2)]
        public string? Error { get; set; }
    }
}
