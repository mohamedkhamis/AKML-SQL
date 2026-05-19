using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) -- M3 task T060. Response to a <see cref="HandshakeRequest"/>.
    /// See <c>specs/021-web-edition/contracts/rpc-handshake.md</c>.
    /// </summary>
    [MessagePackObject]
    public sealed class HandshakeResponse
    {
        /// <summary>One of <see cref="HandshakeStatus"/> values.</summary>
        [Key(0)] public string Status { get; set; } = HandshakeStatus.Ok;

        /// <summary>Engine version (semver).</summary>
        [Key(1)] public string EngineVersion { get; set; } = string.Empty;

        /// <summary>
        /// Protocol version chosen for this connection. Always within the intersection of
        /// client min/max and engine min/max.
        /// </summary>
        [Key(2)] public int ChosenProtocolVersion { get; set; }

        /// <summary>Flat list of capability identifiers the engine supports.</summary>
        [Key(3)] public string[] EngineCapabilities { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// Set ONLY in response to a successful PairingPin handshake. The browser stores
        /// it (wrapped) and uses it on future connections.
        /// </summary>
        [Key(4)] public string? NewBearerToken { get; set; }

        /// <summary>
        /// Server canonical identity for any SQL Server currently selected by this engine,
        /// used as the schema-cache key (M5). Null if engine has no DB connection.
        /// </summary>
        [Key(5)] public string? ServerCanonicalIdentity { get; set; }

        /// <summary>Human-readable detail on error; null on success.</summary>
        [Key(6)] public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Stable string values for <see cref="HandshakeResponse.Status"/>. See
    /// <c>specs/021-web-edition/contracts/rpc-handshake.md</c>.
    /// </summary>
    public static class HandshakeStatus
    {
        public const string Ok = "ok";
        public const string PinInvalid = "pin_invalid";
        public const string PinRequired = "pin_required";
        public const string ProtocolMismatch = "protocol_mismatch";
        public const string ServerBusy = "server_busy";
    }
}
