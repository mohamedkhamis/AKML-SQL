using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) -- M3 task T060. First MessagePack frame on a freshly opened
    /// WebSocket connection between browser and engine. See
    /// <c>specs/021-web-edition/contracts/rpc-handshake.md</c>.
    /// </summary>
    [MessagePackObject]
    public sealed class HandshakeRequest
    {
        /// <summary>One-time PIN supplied for first-time LAN pairing. Mutually exclusive with <see cref="BearerToken"/>.</summary>
        [Key(0)] public string? PairingPin { get; set; }

        /// <summary>Long-lived bearer token from a prior successful pairing.</summary>
        [Key(1)] public string? BearerToken { get; set; }

        /// <summary>Web-edition version string (semver, e.g. "1.0.0").</summary>
        [Key(2)] public string WebVersion { get; set; } = string.Empty;

        /// <summary>Highest protocol version the web client supports.</summary>
        [Key(3)] public int ProtocolVersionMax { get; set; }

        /// <summary>Lowest protocol version the web client supports.</summary>
        [Key(4)] public int ProtocolVersionMin { get; set; }

        /// <summary>Optional human-readable identifier of the browser (for the engine pairing UI).</summary>
        [Key(5)] public string? BrowserLabel { get; set; }

        /// <summary>
        /// Spec 043 follow-up (review, <c>session.copy.v2</c>): a random id the web page makes once and sends on every handshake, so
        /// a reconnect after a link drop is still the same page. A session's password is copied (New query) only into a request
        /// from the page that connected that session. Held in the page's memory only, never stored; null from older clients (the
        /// socket is the page then). Engines that advertise only <c>session.copy.v1</c> ignore it.
        /// </summary>
        [Key(6)] public string? ClientInstanceId { get; set; }
    }
}
