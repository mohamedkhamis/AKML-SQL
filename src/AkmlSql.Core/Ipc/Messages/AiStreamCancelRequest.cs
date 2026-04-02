using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to cancel an in-progress AI streaming operation.
    /// Sent Shell -> Engine.
    /// </summary>
    [MessagePackObject]
    // ReSharper disable once UnusedMember.Global
    public class AiStreamCancelRequest
    {
        /// <summary>The request ID of the streaming operation to cancel.</summary>
        [Key(0)]
        public int RequestId { get; set; }
    }
}
