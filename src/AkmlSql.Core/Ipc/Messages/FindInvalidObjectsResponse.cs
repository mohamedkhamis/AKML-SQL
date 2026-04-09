using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 014, US14 — response from <see cref="FindInvalidObjectsRequest"/>.
    /// Sent Engine → Shell as <see cref="MessageTypes.FindInvalidObjectsResult"/> (190).
    /// May represent a partial chunk; the shell aggregates chunks until
    /// <see cref="IsFinalChunk"/> is true.
    /// </summary>
    [MessagePackObject]
    public class FindInvalidObjectsResponse
    {
        /// <summary>Status code: 0 = Ok, 1 = PermissionDenied, 2 = Error.</summary>
        [Key(0)]
        public int Status { get; set; }

        /// <summary>Records emitted in this chunk. Empty when no invalid objects exist.</summary>
        [Key(1)]
        public InvalidObjectRecord[] Records { get; set; } = [];

        /// <summary>True when this is the last chunk for the scan.</summary>
        [Key(2)]
        public bool IsFinalChunk { get; set; }

        /// <summary>Total number of objects scanned so far (across all chunks).</summary>
        [Key(3)]
        public int TotalScanned { get; set; }

        /// <summary>Error message when <see cref="Status"/> is non-zero.</summary>
        [Key(4)]
        public string? ErrorMessage { get; set; }
    }
}
