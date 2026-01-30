using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AKML.SQL.Shared;
using AKML.SQL.Shared.Contracts;
using Grpc.Core;
using Serilog;

namespace AKML.SQL.SSMS.Services
{
    /// <summary>
    /// gRPC client service for communication with Core service.
    /// Uses Grpc.Core (C-core) for .NET Framework 4.7.2 compatibility.
    /// </summary>
    public class GrpcClientService : IDisposable
    {
        private readonly ILogger _logger;
        private Channel? _channel;
        private BridgeService.BridgeServiceClient? _client;
        private bool _disposed;

        public GrpcClientService(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets whether the client is connected.
        /// </summary>
        public bool IsConnected => _channel?.State == ChannelState.Ready;

        /// <summary>
        /// Connects to the Core service via named pipe.
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            _logger.Information("[{CorrelationId}] Connecting to Core service via named pipe: {PipeName}, Port: {Port}",
                correlationId, AkmlConstants.PipeName, AkmlConstants.DefaultGrpcPort);

            try
            {
                // For Grpc.Core on Windows, use named pipe address
                // Format: dns:///pipename for named pipes, or localhost:port for TCP
                var target = $"localhost:{AkmlConstants.DefaultGrpcPort}";

                _logger.Debug("[{CorrelationId}] Creating gRPC channel to target: {Target}", correlationId, target);

                var channelOptions = new[]
                {
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, 16 * 1024 * 1024),
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, 16 * 1024 * 1024)
                };

                _channel = new Channel(target, ChannelCredentials.Insecure, channelOptions);
                _client = new BridgeService.BridgeServiceClient(_channel);

                _logger.Debug("[{CorrelationId}] Channel created, waiting for Ready state. Current state: {State}",
                    correlationId, _channel.State);

                // Wait for connection with timeout
                var timeout = DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcConnection);
                var startTime = DateTime.UtcNow;
                while (_channel.State != ChannelState.Ready && DateTime.UtcNow < timeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.Verbose("[{CorrelationId}] Channel state: {State}, waiting for state change...",
                        correlationId, _channel.State);
                    await _channel.TryWaitForStateChangedAsync(_channel.State, timeout);
                }

                var elapsed = DateTime.UtcNow - startTime;

                if (_channel.State != ChannelState.Ready)
                {
                    _logger.Error("[{CorrelationId}] Connection timeout after {ElapsedMs}ms. Final state: {State}",
                        correlationId, elapsed.TotalMilliseconds, _channel.State);
                    throw new TimeoutException($"Failed to connect to Core service within timeout. Final state: {_channel.State}");
                }

                _logger.Information("[{CorrelationId}] Connected to Core service successfully in {ElapsedMs}ms",
                    correlationId, elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[{CorrelationId}] Failed to connect to Core service: {ErrorMessage}",
                    correlationId, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Sends a ping request to verify connectivity.
        /// </summary>
        public async Task<PingResponse> PingAsync(CancellationToken cancellationToken = default)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;

            _logger.Debug("[{CorrelationId}] Ping request starting. ClientVersion: {ClientVersion}, SsmsVersion: {SsmsVersion}",
                correlationId, AkmlConstants.ProductVersion, GetSsmsVersion());

            EnsureConnected();

            var request = new PingRequest
            {
                ClientVersion = AkmlConstants.ProductVersion,
                SsmsVersion = GetSsmsVersion()
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall));

            try
            {
                var response = await _client!.PingAsync(request, callOptions);
                var elapsed = DateTime.UtcNow - startTime;

                _logger.Information("[{CorrelationId}] Ping completed in {ElapsedMs}ms. Success: {Success}, ServerVersion: {ServerVersion}",
                    correlationId, elapsed.TotalMilliseconds, response.Success, response.ServerVersion);

                return response;
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.Error(ex, "[{CorrelationId}] Ping failed after {ElapsedMs}ms: {ErrorMessage}",
                    correlationId, elapsed.TotalMilliseconds, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Synchronizes text content with Core service.
        /// </summary>
        public async Task<SyncTextResponse> SyncTextAsync(
            string documentId,
            string text,
            int cursorLine,
            int cursorColumn,
            string? connectionString,
            CancellationToken cancellationToken = default)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;

            _logger.Debug("[{CorrelationId}] SyncText starting. DocumentId: {DocumentId}, TextLength: {TextLength}, Cursor: ({Line},{Column}), HasConnection: {HasConnection}",
                correlationId, documentId, text?.Length ?? 0, cursorLine, cursorColumn, !string.IsNullOrEmpty(connectionString));

            EnsureConnected();

            var request = new SyncTextRequest
            {
                DocumentId = documentId,
                FullText = text,
                CursorLine = cursorLine,
                CursorColumn = cursorColumn,
                ConnectionString = connectionString ?? string.Empty,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall));

            try
            {
                var response = await _client!.SyncTextAsync(request, callOptions);
                var elapsed = DateTime.UtcNow - startTime;

                _logger.Debug("[{CorrelationId}] SyncText completed in {ElapsedMs}ms. Success: {Success}, DocumentId: {DocumentId}",
                    correlationId, elapsed.TotalMilliseconds, response.Success, documentId);

                return response;
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.Error(ex, "[{CorrelationId}] SyncText failed after {ElapsedMs}ms for document {DocumentId}: {ErrorMessage}",
                    correlationId, elapsed.TotalMilliseconds, documentId, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Gets code completions at the specified position.
        /// </summary>
        public async Task<CompletionResponse> GetCompletionsAsync(
            string documentId,
            string text,
            int cursorLine,
            int cursorColumn,
            string? triggerCharacter,
            string? connectionString,
            int maxItems = 50,
            CancellationToken cancellationToken = default)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;
            var triggerKind = string.IsNullOrEmpty(triggerCharacter)
                ? CompletionTriggerKind.Invoked
                : CompletionTriggerKind.Character;

            _logger.Debug("[{CorrelationId}] GetCompletions starting. DocumentId: {DocumentId}, Cursor: ({Line},{Column}), TriggerKind: {TriggerKind}, TriggerChar: '{TriggerChar}', MaxItems: {MaxItems}",
                correlationId, documentId, cursorLine, cursorColumn, triggerKind, triggerCharacter ?? "", maxItems);

            EnsureConnected();

            var request = new CompletionRequest
            {
                DocumentId = documentId,
                Text = text,
                CursorLine = cursorLine,
                CursorColumn = cursorColumn,
                TriggerCharacter = triggerCharacter ?? string.Empty,
                ConnectionString = connectionString ?? string.Empty,
                TriggerKind = triggerKind,
                MaxItems = maxItems
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall));

            try
            {
                var response = await _client!.GetCompletionsAsync(request, callOptions);
                var elapsed = DateTime.UtcNow - startTime;

                _logger.Information("[{CorrelationId}] GetCompletions completed in {ElapsedMs}ms. ItemCount: {ItemCount}, IsIncomplete: {IsIncomplete}",
                    correlationId, elapsed.TotalMilliseconds, response.Items.Count, response.IsIncomplete);

                if (response.Items.Count > 0)
                {
                    var firstFive = response.Items.Take(5).Select(i => i.Label).ToArray();
                    _logger.Debug("[{CorrelationId}] First 5 completions: {Completions}",
                        correlationId, string.Join(", ", firstFive));
                }

                return response;
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.Error(ex, "[{CorrelationId}] GetCompletions failed after {ElapsedMs}ms: {ErrorMessage}",
                    correlationId, elapsed.TotalMilliseconds, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Parses SQL and returns AST information.
        /// </summary>
        public async Task<ParseResponse> ParseSqlAsync(
            string documentId,
            string text,
            bool includeAst = false,
            CancellationToken cancellationToken = default)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;

            _logger.Debug("[{CorrelationId}] ParseSql starting. DocumentId: {DocumentId}, TextLength: {TextLength}, IncludeAst: {IncludeAst}",
                correlationId, documentId, text?.Length ?? 0, includeAst);

            EnsureConnected();

            var request = new ParseRequest
            {
                DocumentId = documentId,
                Text = text,
                IncludeAst = includeAst
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall));

            try
            {
                var response = await _client!.ParseSqlAsync(request, callOptions);
                var elapsed = DateTime.UtcNow - startTime;

                _logger.Information("[{CorrelationId}] ParseSql completed in {ElapsedMs}ms. Success: {Success}, HasErrors: {HasErrors}, StatementCount: {StatementCount}",
                    correlationId, elapsed.TotalMilliseconds, response.Success, response.HasErrors, response.Statements.Count);

                if (response.Diagnostics.Count > 0)
                {
                    foreach (var diag in response.Diagnostics.Take(3))
                    {
                        _logger.Warning("[{CorrelationId}] Parse diagnostic [{Severity}] at ({Line},{Column}): {Message}",
                            correlationId, diag.Severity, diag.StartLine, diag.StartColumn, diag.Message);
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.Error(ex, "[{CorrelationId}] ParseSql failed after {ElapsedMs}ms: {ErrorMessage}",
                    correlationId, elapsed.TotalMilliseconds, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Formats SQL code.
        /// </summary>
        public async Task<FormatResponse> FormatSqlAsync(
            string text,
            FormatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;

            var effectiveOptions = options ?? new FormatOptions
            {
                IndentSize = 4,
                UseTabs = false,
                KeywordCasing = KeywordCasing.Uppercase
            };

            _logger.Debug("[{CorrelationId}] FormatSql starting. TextLength: {TextLength}, IndentSize: {IndentSize}, UseTabs: {UseTabs}, KeywordCasing: {KeywordCasing}",
                correlationId, text?.Length ?? 0, effectiveOptions.IndentSize, effectiveOptions.UseTabs, effectiveOptions.KeywordCasing);

            EnsureConnected();

            var request = new FormatRequest
            {
                Text = text,
                Options = effectiveOptions
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall));

            try
            {
                var response = await _client!.FormatSqlAsync(request, callOptions);
                var elapsed = DateTime.UtcNow - startTime;

                _logger.Information("[{CorrelationId}] FormatSql completed in {ElapsedMs}ms. Success: {Success}, InputLength: {InputLength}, OutputLength: {OutputLength}",
                    correlationId, elapsed.TotalMilliseconds, response.Success, text?.Length ?? 0, response.FormattedText?.Length ?? 0);

                return response;
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.Error(ex, "[{CorrelationId}] FormatSql failed after {ElapsedMs}ms: {ErrorMessage}",
                    correlationId, elapsed.TotalMilliseconds, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Gets database metadata.
        /// </summary>
        public async Task<MetadataResponse> GetMetadataAsync(
            string connectionString,
            MetadataScope scope = MetadataScope.All,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var startTime = DateTime.UtcNow;

            // Mask connection string for logging (show server/database only)
            var maskedConnection = MaskConnectionString(connectionString);
            _logger.Debug("[{CorrelationId}] GetMetadata starting. Connection: {Connection}, Scope: {Scope}, ForceRefresh: {ForceRefresh}",
                correlationId, maskedConnection, scope, forceRefresh);

            EnsureConnected();

            var request = new MetadataRequest
            {
                ConnectionString = connectionString,
                Scope = scope,
                ForceRefresh = forceRefresh
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall * 2)); // Metadata can take longer

            try
            {
                var response = await _client!.GetMetadataAsync(request, callOptions);
                var elapsed = DateTime.UtcNow - startTime;

                var tableCount = response.Tables.Count;
                var viewCount = response.Tables.Count(t => t.IsView);
                var procCount = response.Procedures.Count;

                _logger.Information("[{CorrelationId}] GetMetadata completed in {ElapsedMs}ms. Success: {Success}, TableCount: {TableCount}, ViewCount: {ViewCount}, ProcCount: {ProcCount}",
                    correlationId, elapsed.TotalMilliseconds, response.Success, tableCount - viewCount, viewCount, procCount);

                return response;
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.Error(ex, "[{CorrelationId}] GetMetadata failed after {ElapsedMs}ms for {Connection}: {ErrorMessage}",
                    correlationId, elapsed.TotalMilliseconds, maskedConnection, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Masks sensitive parts of connection string for logging.
        /// </summary>
        private static string MaskConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return "(empty)";

            try
            {
                var parts = connectionString.Split(';')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(p =>
                    {
                        var kv = p.Split(new[] { '=' }, 2);
                        if (kv.Length != 2) return p;

                        var key = kv[0].Trim().ToLowerInvariant();
                        // Only show server and database, mask everything else
                        if (key == "server" || key == "data source" || key == "database" || key == "initial catalog")
                            return p;
                        return $"{kv[0]}=***";
                    });

                return string.Join("; ", parts);
            }
            catch
            {
                return "(invalid connection string)";
            }
        }

        private void EnsureConnected()
        {
            if (_client == null || _channel == null || _channel.State == ChannelState.Shutdown)
            {
                throw new InvalidOperationException("Not connected to Core service");
            }
        }

        private static string GetSsmsVersion()
        {
            try
            {
                // Try to get SSMS version from environment or registry
                var ssmsPath = Environment.GetEnvironmentVariable("SSMS_PATH");
                if (!string.IsNullOrEmpty(ssmsPath))
                {
                    var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(ssmsPath);
                    return fileVersion.FileVersion ?? "Unknown";
                }
            }
            catch
            {
                // Ignore errors
            }

            return "Unknown";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _channel?.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error disposing gRPC channel");
            }
        }
    }
}
