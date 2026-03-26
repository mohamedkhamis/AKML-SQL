using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response confirming whether a session snapshot was deleted successfully.
    /// Sent Engine → Shell.
    /// </summary>
    [MessagePackObject]
    public class SessionDeleteResponse
    {
        /// <summary>Whether the delete completed without errors.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Error message if <see cref="Success"/> is <c>false</c>.</summary>
        [Key(1)]
        public string? Error { get; set; }
    }
}
