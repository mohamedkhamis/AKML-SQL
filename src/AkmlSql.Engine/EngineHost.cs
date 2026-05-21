using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

                // Spec 022 (M0 closure). The composition root builds services, context and
                // router; the transport (T027) implements IRpcTransport -- it owns only pipe
                // lifecycle + frame I/O and forwards each decoded message to the router via the
                // RequestReceived event.
                var composition = EngineComposition.Build();
                await using var transport = new NamedPipeTransport(pipeName);
                transport.RequestReceived += async (msg, ct) =>
                {
                    var response = await composition.Router.RouteAsync(msg, composition.Context, ct);
                    if (response == null && !composition.Router.IsRegistered(msg.MessageType))
                        Log.Warning("Unknown message type: {Type}", msg.MessageType);
                    return response;
                };
                await transport.RunAsync(token);
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
