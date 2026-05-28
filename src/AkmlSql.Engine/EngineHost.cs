using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Logging;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using AkmlSql.Formatting.Profiles;
using Serilog;

namespace AkmlSql.Engine
{
    /// <summary>
    /// Spec 021 (web edition) -- M0 task T022. Engine bootstrap facade. Consolidates the
    /// startup sequence (logger init, AI key-decryptor wiring, parent-process monitoring,
    /// pending-import processing, NamedPipeTransport construction + run, shutdown) into one
    /// reusable entry point. <c>Program.Main</c> parses CLI args and hands off to
    /// <see cref="RunAsync"/>; other consumers (tests, the future web-mode launcher) can
    /// call <see cref="RunAsync"/> directly without re-implementing the boilerplate.
    ///
    /// <para>
    /// Service construction and handler registration live in <see cref="EngineComposition"/> +
    /// <c>EngineHandlerRegistry</c> (spec 022 closure); <see cref="NamedPipeTransport"/> is now
    /// pure frame I/O. <see cref="RpcRouter.RegisterAllInAssembly"/> offers an additional
    /// reflective path for the dependency-free handler subset (used by in-process tests).
    /// </para>
    /// </summary>
    public static class EngineHost
    {
        /// <summary>
        /// Run the engine end-to-end against the given named pipe. Returns a process exit
        /// code: 0 = clean shutdown, 2 = crash. Caller is responsible for parsing CLI args.
        /// </summary>
        public static async Task<int> RunAsync(
            string pipeName,
            int parentPid,
            CancellationToken externalToken = default)
        {
            if (string.IsNullOrEmpty(pipeName)) throw new ArgumentException("pipeName is required.", nameof(pipeName));

            LoggerFactory.Initialize();
            Log.Information("AkmlSql.Engine starting. Pipe={Pipe}, ParentPid={ParentPid}", pipeName, parentPid);

            // Spec 021 T121 -- wire the AkmlSql.AI provider factory's KeyDecryptor hook to
            // the Windows-only DPAPI CredentialManager so the named-pipe path keeps the
            // previous behaviour exactly. The web edition leaves the hook at its default
            // identity (Web Crypto unwraps the key before calling).
            Ai.Providers.AiProviderFactory.KeyDecryptor =
                encryptedKey => Ai.Security.CredentialManager.Decrypt(encryptedKey!);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = cts.Token;

            // Orphan protection: monitor parent process.
            if (parentPid > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var parent = Process.GetProcessById(parentPid);
                        while (!parent.HasExited && !token.IsCancellationRequested)
                        {
                            await Task.Delay(2000, token);
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Parent process not found -- already exited.
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error monitoring parent process {Pid}", parentPid);
                    }

                    Log.Warning("Parent process {Pid} exited. Engine shutting down.", parentPid);
                    try { await cts.CancelAsync(); } catch (ObjectDisposedException) { }
                }, token);
            }

            try
            {
                // T035 -- process pending SQL Prompt import before starting the RPC server.
                ProcessPendingImports();

                // Spec 026 (M4 closure) FR-013a: load settings up front so the bridge-auth
                // composition can enforce the pairing PIN in LAN mode. ConfigManager.Load() is
                // idempotent -- the WebSocket transport below reuses the same settings instance.
                var settings = ConfigManager.Load();
                var bridgeAuth = BuildBridgeAuth(settings.Bridge);

                // Spec 022 (M0 closure). The composition root builds services, context and
                // router; the transport (T027) implements IRpcTransport -- it owns only pipe
                // lifecycle + frame I/O and forwards each decoded message to the router via the
                // RequestReceived event. Spec 026 FR-013a: the LAN-mode handshake handler (wired
                // to a live PairingService + BearerTokenStore) is supplied here; loopback / no-bridge
                // passes null and the registry falls back to the parameterless auto-accept handler.
                var composition = EngineComposition.Build(bridgeAuth.Handshake);

                // Spec 026 (M4 closure) FR-008: in LAN mode, persist the minted PIN to
                // %CommonAppData%/AKML SQL Web/pairing-pin.txt so the installer's Web_PostInstall
                // can surface it in INSTALL-SUMMARY.txt. The one-shot publish captures the initial
                // PIN (minted inside the PairingService ctor, before this subscription attaches).
                if (bridgeAuth.Pairing != null)
                {
                    var pinFile = new Pairing.PairingPinFile(Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "AKML SQL Web", "pairing-pin.txt"));
                    bridgeAuth.Pairing.PinChanged += (_, pin) => pinFile.Publish(pin);
                    pinFile.Publish(bridgeAuth.Pairing.CurrentPin);
                }

                async Task<RpcMessage?> RouteAsync(RpcMessage msg, CancellationToken ct)
                {
                    var response = await composition.Router.RouteAsync(msg, composition.Context, ct);
                    if (response == null && !composition.Router.IsRegistered(msg.MessageType))
                        Log.Warning("Unknown message type: {Type}", msg.MessageType);
                    return response;
                }

                await using var transport = new NamedPipeTransport(pipeName);
                transport.RequestReceived += RouteAsync;

                // Spec 025 (M3 closure) FR-027: when config.Bridge.Enabled, compose a
                // WebSocketTransport alongside the named pipe. Both share the same router,
                // so SSMS plugin (pipe) and web edition (WebSocket) serve identical
                // handler chains. When Bridge is absent or disabled, the engine behaves
                // exactly like the IDE-plugin-only deployment.
                var wsTransport = BuildWebSocketTransport(settings.Bridge);
                if (wsTransport != null)
                {
                    wsTransport.RequestReceived += RouteAsync;
                    await wsTransport.StartAsync(token).ConfigureAwait(false);
                    Log.Information("Bridge enabled: WebSocketTransport composed alongside named pipe.");
                }

                try
                {
                    await transport.RunAsync(token);
                }
                finally
                {
                    if (wsTransport != null)
                    {
                        await wsTransport.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log.Information("Engine shutdown requested.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Engine crashed.");
                return 2;
            }
            finally
            {
                LoggerFactory.Shutdown();
            }

            return 0;
        }

        /// <summary>
        /// Spec 025 (M3 closure) FR-027: builds the optional <see cref="WebSocketTransport"/>
        /// composed alongside the named pipe. Returns <c>null</c> when the bridge is disabled
        /// or the config section is absent — preserving IDE-plugin-only behaviour. Exposed
        /// <c>internal</c> for <c>EngineHostTests</c>.
        /// </summary>
        internal static WebSocketTransport? BuildWebSocketTransport(BridgeOptions bridge)
        {
            if (bridge == null || !bridge.Enabled)
            {
                return null;
            }

            var options = new WebSocketTransportOptions
            {
                BindAddress = bridge.BindAddress,
                Port = bridge.Port,
                TlsCertPath = bridge.TlsCertPath,
                TlsCertPasswordRef = bridge.TlsCertPasswordRef,
                TokenStorePath = bridge.TokenStorePath,
                TokenTtl = TimeSpan.FromDays(bridge.TokenTtlDays),
                RequirePairingToken = !bridge.IsLoopback,
            };
            return new WebSocketTransport(options);
        }

        /// <summary>
        /// Spec 026 (M4 closure) FR-013a / FR-013b. Builds the bridge handshake handler:
        /// LAN mode enforces the pairing PIN; loopback / disabled / absent auto-accepts.
        /// Exposed <c>internal</c> for <c>EngineHostTests</c>.
        /// </summary>
        /// <remarks>
        /// LAN mode (bridge enabled + non-loopback) constructs a live <see cref="Pairing.PairingService"/>
        /// + <see cref="Pairing.BearerTokenStore"/> and wires the full
        /// <see cref="Handlers.Handshake.HandshakeHandler"/> constructor (FR-013a). The
        /// <c>pinValidator</c> keys <see cref="Pairing.PairingService"/>'s per-source rate limit on the
        /// transport-published remote IP (<see cref="Pairing.BridgeSourceIp"/>). Loopback / disabled /
        /// absent returns the parameterless auto-accept handler (FR-013b) with a null
        /// <see cref="BridgeAuth.Pairing"/>, so no PIN file is written and localhost needs no PIN.
        /// </remarks>
        internal static BridgeAuth BuildBridgeAuth(Core.Config.BridgeOptions? bridge)
        {
            if (bridge == null || !bridge.Enabled || bridge.IsLoopback)
            {
                return new BridgeAuth { Handshake = new Handlers.Handshake.HandshakeHandler() };
            }

            var tokenStorePath = string.IsNullOrWhiteSpace(bridge.TokenStorePath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "AKML SQL Web", "tokens.json")
                : bridge.TokenStorePath;
            var ttlDays = bridge.TokenTtlDays > 0 ? bridge.TokenTtlDays : 90;

            var pairing = new Pairing.PairingService();
            var tokens = new Pairing.BearerTokenStore(tokenStorePath, TimeSpan.FromDays(ttlDays));

            var handshake = new Handlers.Handshake.HandshakeHandler(
                pairingRequired: () => true,
                pinValidator: pin => pairing.ValidatePin(
                    Pairing.BridgeSourceIp.Current?.ToString() ?? "ws", pin) == Pairing.PinAttemptResult.Valid,
                bearerValidator: token => tokens.Validate(token),
                bearerMinter: label => tokens.Mint(label),
                serverCanonicalIdentityProvider: () => null);

            return new BridgeAuth { Handshake = handshake, Pairing = pairing, Tokens = tokens };
        }

        /// <summary>
        /// Spec 026 (M4 closure). The bridge's handshake handler plus the live pairing services it
        /// was wired to (both null in loopback / no-bridge mode). <c>EngineHost</c> uses
        /// <see cref="Pairing"/> to wire the PIN-file writer; <c>EngineHostTests</c> reads
        /// <c>Pairing.CurrentPin</c> to drive the auth-composition matrix.
        /// </summary>
        internal sealed class BridgeAuth
        {
            public required Handlers.Handshake.HandshakeHandler Handshake { get; init; }
            public Pairing.PairingService? Pairing { get; init; }
            public Pairing.BearerTokenStore? Tokens { get; init; }
        }

        /// <summary>
        /// T035: Checks for pending-import.json written by the installer, imports each
        /// SQL Prompt style file via SqlPromptImporter, saves the resulting profiles, and
        /// deletes the manifest file.
        /// </summary>
        private static void ProcessPendingImports()
        {
            try
            {
                var appDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL");
                var pendingPath = Path.Combine(appDataFolder, "pending-import.json");

                if (!File.Exists(pendingPath))
                    return;

                Log.Information("Found pending-import.json -- processing SQL Prompt style imports.");

                var json = File.ReadAllText(pendingPath);
                var manifest = JsonSerializer.Deserialize<ImportManifest>(json);
                if (manifest?.Files == null || manifest.Files.Count == 0)
                {
                    Log.Warning("pending-import.json contains no files. Removing manifest.");
                    File.Delete(pendingPath);
                    return;
                }

                var profileManager = ProfileManager.CreateDefault();
                var importedCount = 0;
                var failedCount = 0;

                foreach (var filePath in manifest.Files)
                {
                    try
                    {
                        if (!Path.IsPathRooted(filePath))
                        {
                            Log.Warning("Skipping non-absolute import path: {Path}", filePath);
                            failedCount++;
                            continue;
                        }

                        if (!File.Exists(filePath))
                        {
                            Log.Warning("Staged import file not found: {Path}", filePath);
                            failedCount++;
                            continue;
                        }

                        // Derive a profile name from the file name (without extension).
                        var fileName = Path.GetFileNameWithoutExtension(filePath);
                        var profileName = $"SQL Prompt - {fileName}";

                        Log.Information("Importing SQL Prompt style: {File} as '{ProfileName}'",
                            filePath, profileName);

                        var result = SqlPromptImporter.ImportFromFile(filePath, profileName);
                        profileManager.Save(result.Profile);

                        Log.Information("Imported '{ProfileName}': {Mapped} options mapped, {Unmapped} unmapped",
                            profileName, result.MappedCount, result.UnmappedCount);

                        if (result.UnmappedOptions.Count > 0)
                        {
                            Log.Debug("Unmapped options for '{ProfileName}': {Options}",
                                profileName, string.Join(", ", result.UnmappedOptions));
                        }

                        importedCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to import SQL Prompt style: {Path}", filePath);
                        failedCount++;
                    }
                }

                // Clean up: delete the manifest and staging directory.
                File.Delete(pendingPath);
                Log.Information("Deleted pending-import.json.");

                var stagingDir = Path.Combine(appDataFolder, "import-staging");
                if (Directory.Exists(stagingDir))
                {
                    try
                    {
                        Directory.Delete(stagingDir, recursive: true);
                        Log.Information("Cleaned up import-staging directory.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to clean up import-staging directory.");
                    }
                }

                Log.Information("SQL Prompt import complete: {Imported} imported, {Failed} failed.",
                    importedCount, failedCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing pending SQL Prompt imports.");
                // Non-fatal -- don't prevent engine startup if import fails. Try to clean
                // up the manifest to avoid retrying on every startup.
                try
                {
                    var pendingPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "AKML SQL", "pending-import.json");
                    if (File.Exists(pendingPath))
                        File.Delete(pendingPath);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }

        /// <summary>
        /// JSON model for the pending-import.json manifest written by the installer.
        /// Example: {"source":"SQL Prompt","files":["C:\\...\\file.sqlpromptstylev2"]}
        /// </summary>
        private sealed class ImportManifest
        {
            [System.Text.Json.Serialization.JsonPropertyName("source")]
            public string? Source { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("files")]
            public System.Collections.Generic.List<string>? Files { get; set; }
        }
    }
}
