using System;

namespace AkmlSql.Engine.Transports
{
    /// <summary>
    /// Spec 021 (web edition) -- M3 task T057. Configuration for the engine's WebSocket
    /// transport. Loaded from <c>%AppData%/AKML SQL Web/config.json</c> by the engine host
    /// (the named-pipe transport's options live on the existing `NamedPipeTransport`; this is a
    /// parallel set for the new transport).
    ///
    /// Two intended modes:
    /// * Localhost: BindAddress = "127.0.0.1", RequirePairingToken = false, TLS off.
    ///   Browser connects via ws://127.0.0.1:Port/.
    /// * LAN exposed: BindAddress = "0.0.0.0", RequirePairingToken = true, TLS REQUIRED.
    ///   Browser connects via wss://&lt;host&gt;:Port/ after a one-time PIN handshake.
    ///   See clarification 1 in spec.md and contracts/pairing-flow.md.
    /// </summary>
    public sealed class WebSocketTransportOptions
    {
        /// <summary>IP address to bind. <c>"127.0.0.1"</c> for localhost; <c>"0.0.0.0"</c> for LAN.</summary>
        public string BindAddress { get; init; } = "127.0.0.1";

        /// <summary>TCP port. Default <c>47291</c> per contracts/rpc-handshake.md.</summary>
        public int Port { get; init; } = 47291;

        /// <summary>
        /// When <see langword="true"/>, the transport rejects any handshake that does not
        /// include a valid <c>PairingPin</c> or <c>BearerToken</c>. Implementations of the
        /// pairing service supply the validation. Forced true at construction time when
        /// <see cref="BindAddress"/> is not a loopback address.
        /// </summary>
        public bool RequirePairingToken { get; init; }

        /// <summary>
        /// On-disk JSON file storing hashed bearer tokens. Default
        /// <c>%AppData%/AKML SQL Web/tokens.json</c>. See data-model.md E3 (PairingToken)
        /// and contracts/pairing-flow.md.
        /// </summary>
        public string TokenStorePath { get; init; } = string.Empty;

        /// <summary>Default <c>90 days</c> per contracts/pairing-flow.md § Token revocation.</summary>
        public TimeSpan TokenTtl { get; init; } = TimeSpan.FromDays(90);

        /// <summary>
        /// Path to a PFX containing the LAN-mode self-signed TLS certificate (FR-013a;
        /// clarification 1). Required when <see cref="BindAddress"/> is not a loopback
        /// address. The installer (T087) generates and writes this. Ignored for
        /// localhost-only installs. T058 will wire this up to Kestrel HTTPS.
        /// </summary>
        public string? TlsCertPath { get; init; }

        /// <summary>
        /// Optional reference (e.g. environment variable name) to the PFX password. Kept
        /// out of <c>config.json</c> so the cert password does not sit on disk in plain.
        /// </summary>
        public string? TlsCertPasswordRef { get; init; }

        /// <summary>True if the bind address is a loopback IP.</summary>
        public bool IsLoopback =>
            BindAddress == "127.0.0.1" || BindAddress == "::1" || BindAddress == "localhost";
    }
}
