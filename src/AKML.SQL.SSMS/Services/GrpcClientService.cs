using System;
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
            _logger.Information("Connecting to Core service via named pipe: {PipeName}", AkmlConstants.PipeName);

            try
            {
                // For Grpc.Core on Windows, use named pipe address
                // Format: dns:///pipename for named pipes, or localhost:port for TCP
                var target = $"localhost:{AkmlConstants.DefaultGrpcPort}";
                
                // In production, you'd use named pipes:
                // var target = $"dns:///{AkmlConstants.PipeName}";

                var channelOptions = new[]
                {
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, 16 * 1024 * 1024),
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, 16 * 1024 * 1024)
                };

                _channel = new Channel(target, ChannelCredentials.Insecure, channelOptions);
                _client = new BridgeService.BridgeServiceClient(_channel);

                // Wait for connection with timeout
                var timeout = DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcConnection);
                while (_channel.State != ChannelState.Ready && DateTime.UtcNow < timeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await _channel.TryWaitForStateChangedAsync(_channel.State, timeout);
                }

                if (_channel.State != ChannelState.Ready)
                {
                    throw new TimeoutException("Failed to connect to Core service within timeout");
                }

                _logger.Information("Connected to Core service");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to connect to Core service");
                throw;
            }
        }

        /// <summary>
        /// Sends a ping request to verify connectivity.
        /// </summary>
        public async Task<PingResponse> PingAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            var request = new PingRequest
            {
                ClientVersion = AkmlConstants.ProductVersion,
                SsmsVersion = GetSsmsVersion()
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall));

            return await _client!.PingAsync(request, callOptions);
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

            return await _client!.SyncTextAsync(request, callOptions);
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
            EnsureConnected();

            var request = new CompletionRequest
            {
                DocumentId = documentId,
                Text = text,
                CursorLine = cursorLine,
                CursorColumn = cursorColumn,
                TriggerCharacter = triggerCharacter ?? string.Empty,
                ConnectionString = connectionString ?? string.Empty,
                TriggerKind = string.IsNullOrEmpty(triggerCharacter) 
                    ? CompletionTriggerKind.Invoked 
                    : CompletionTriggerKind.Character,
                MaxItems = maxItems
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall));

            return await _client!.GetCompletionsAsync(request, callOptions);
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

            return await _client!.ParseSqlAsync(request, callOptions);
        }

        /// <summary>
        /// Formats SQL code.
        /// </summary>
        public async Task<FormatResponse> FormatSqlAsync(
            string text,
            FormatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            var request = new FormatRequest
            {
                Text = text,
                Options = options ?? new FormatOptions
                {
                    IndentSize = 4,
                    UseTabs = false,
                    KeywordCasing = KeywordCasing.Uppercase
                }
            };

            var callOptions = new CallOptions(
                cancellationToken: cancellationToken,
                deadline: DateTime.UtcNow.AddMilliseconds(AkmlConstants.Timeouts.GrpcCall));

            return await _client!.FormatSqlAsync(request, callOptions);
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

            return await _client!.GetMetadataAsync(request, callOptions);
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
