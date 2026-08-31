using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// The session-suppression list after the requested change was applied. Every action returns
    /// the full list, so the caller never has to track it locally or ask again.
    /// Sent Engine -> Shell as MessageType 136 (SessionSuppressionResult). Pairs with request 36.
    /// </summary>
    [MessagePackObject]
    public class SessionSuppressionResponse
    {
        /// <summary>Whether the request was applied.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Every rule currently suppressed for this session, sorted by id.</summary>
        [Key(1)]
        public string[] SuppressedRules { get; set; } = [];

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(2)]
        public string? Error { get; set; }
    }
}
