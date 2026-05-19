using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Transports;

namespace AkmlSql.Engine.Handlers.Handshake
{
    /// <summary>
    /// Spec 021 (web edition) -- M3 task T060. First-frame handshake for the WebSocket
    /// bridge. Validates the protocol-version range, optional pairing PIN / bearer token,
    /// and emits the engine's capability list. See contracts/rpc-handshake.md.
    ///
    /// Wiring (M3 follow-up): for LAN-mode connections the constructor will take a
    /// <c>PairingService</c> (T063) + <c>BearerTokenStore</c> (T064) and validate the
    /// inbound PIN / token. Until those land, this handler accepts unauthenticated
    /// localhost handshakes only -- the transport enforces loopback-only by default.
    /// </summary>
    public sealed class HandshakeHandler : IRpcRequestHandler<HandshakeRequest, HandshakeResponse>
    {
        /// <summary>Engine version string baked from the assembly informational-version attribute.</summary>
        private static readonly string EngineVersion = ResolveEngineVersion();

        private readonly Func<bool> _pairingRequiredProvider;
        private readonly Func<string, bool> _pinValidator;
        private readonly Func<string, bool> _bearerValidator;
        private readonly Func<string, string?> _bearerMinter;
        private readonly Func<string?> _serverCanonicalIdentityProvider;

        /// <summary>
        /// Construct a localhost-only handshake handler that accepts any inbound. Used by
        /// the engine's localhost transport before the pairing infrastructure lands.
        /// </summary>
        public HandshakeHandler()
            : this(
                pairingRequired: () => false,
                pinValidator: _ => true,
                bearerValidator: _ => true,
                bearerMinter: _ => null,
                serverCanonicalIdentityProvider: () => null)
        {
        }

        /// <summary>
        /// Full constructor: takes callbacks for the pairing-service integration that
        /// lands in T063+. The handler does not depend on the concrete service types so
        /// it can be unit-tested without standing up the full engine.
        /// </summary>
        public HandshakeHandler(
            Func<bool> pairingRequired,
            Func<string, bool> pinValidator,
            Func<string, bool> bearerValidator,
            Func<string, string?> bearerMinter,
            Func<string?> serverCanonicalIdentityProvider)
        {
            _pairingRequiredProvider = pairingRequired ?? throw new ArgumentNullException(nameof(pairingRequired));
            _pinValidator = pinValidator ?? throw new ArgumentNullException(nameof(pinValidator));
            _bearerValidator = bearerValidator ?? throw new ArgumentNullException(nameof(bearerValidator));
            _bearerMinter = bearerMinter ?? throw new ArgumentNullException(nameof(bearerMinter));
            _serverCanonicalIdentityProvider = serverCanonicalIdentityProvider ?? throw new ArgumentNullException(nameof(serverCanonicalIdentityProvider));
        }

        public int RequestMessageType => MessageTypes.HandshakeRequest;
        public int ResponseMessageType => MessageTypes.HandshakeResponse;

        public Task<HandshakeResponse> HandleAsync(HandshakeRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // 1. Protocol-version negotiation: pick the highest version both peers support.
            var chosen = Math.Min(request.ProtocolVersionMax, Capabilities.ProtocolVersionMax);
            if (chosen < Math.Max(request.ProtocolVersionMin, Capabilities.ProtocolVersionMin))
            {
                return Task.FromResult(new HandshakeResponse
                {
                    Status = HandshakeStatus.ProtocolMismatch,
                    EngineVersion = EngineVersion,
                    ErrorMessage =
                        $"No protocol-version overlap. Client wants {request.ProtocolVersionMin}..{request.ProtocolVersionMax}; " +
                        $"engine supports {Capabilities.ProtocolVersionMin}..{Capabilities.ProtocolVersionMax}.",
                });
            }

            // 2. Pairing validation (LAN mode).
            if (_pairingRequiredProvider())
            {
                if (!string.IsNullOrEmpty(request.PairingPin))
                {
                    if (!_pinValidator(request.PairingPin!))
                    {
                        return Task.FromResult(new HandshakeResponse
                        {
                            Status = HandshakeStatus.PinInvalid,
                            EngineVersion = EngineVersion,
                            ChosenProtocolVersion = chosen,
                            ErrorMessage = "Pairing PIN was wrong or expired.",
                        });
                    }

                    var minted = _bearerMinter(request.BrowserLabel ?? string.Empty);
                    return Task.FromResult(new HandshakeResponse
                    {
                        Status = HandshakeStatus.Ok,
                        EngineVersion = EngineVersion,
                        ChosenProtocolVersion = chosen,
                        EngineCapabilities = Capabilities.Current.ToArray(),
                        NewBearerToken = minted,
                        ServerCanonicalIdentity = _serverCanonicalIdentityProvider(),
                    });
                }

                if (!string.IsNullOrEmpty(request.BearerToken))
                {
                    if (!_bearerValidator(request.BearerToken!))
                    {
                        return Task.FromResult(new HandshakeResponse
                        {
                            Status = HandshakeStatus.PinRequired,
                            EngineVersion = EngineVersion,
                            ChosenProtocolVersion = chosen,
                            ErrorMessage =
                                "Bearer token unknown or revoked. Re-pair with a fresh PIN " +
                                "from the engine's Pairing UI.",
                        });
                    }

                    return Task.FromResult(new HandshakeResponse
                    {
                        Status = HandshakeStatus.Ok,
                        EngineVersion = EngineVersion,
                        ChosenProtocolVersion = chosen,
                        EngineCapabilities = Capabilities.Current.ToArray(),
                        ServerCanonicalIdentity = _serverCanonicalIdentityProvider(),
                    });
                }

                // LAN mode but neither PIN nor token supplied.
                return Task.FromResult(new HandshakeResponse
                {
                    Status = HandshakeStatus.PinRequired,
                    EngineVersion = EngineVersion,
                    ChosenProtocolVersion = chosen,
                    ErrorMessage = "LAN mode requires a pairing PIN or bearer token.",
                });
            }

            // 3. Localhost mode: accept any inbound.
            return Task.FromResult(new HandshakeResponse
            {
                Status = HandshakeStatus.Ok,
                EngineVersion = EngineVersion,
                ChosenProtocolVersion = chosen,
                EngineCapabilities = Capabilities.Current.ToArray(),
                ServerCanonicalIdentity = _serverCanonicalIdentityProvider(),
            });
        }

        private static string ResolveEngineVersion()
        {
            var asm = typeof(HandshakeHandler).Assembly;
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(info) ? asm.GetName().Version?.ToString() ?? "0.0.0" : info!;
        }
    }
}
