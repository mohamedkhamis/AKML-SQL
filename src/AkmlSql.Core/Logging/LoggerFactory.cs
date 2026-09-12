using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using Serilog;
using Serilog.Events;

namespace AkmlSql.Core.Logging
{
    public static class LoggerFactory
    {
        private static int _initialized;

        public static void Initialize()
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            {
                return;
            }

            var logPath = Path.Combine(Constants.LogsPath, "akmlsql-.log");

            Directory.CreateDirectory(Constants.LogsPath);

            // Read minimum log level + telemetry settings from config JSON directly (avoids circular dependency with ConfigManager)
            var minLevel = LogEventLevel.Debug;
            var telemetryEnabled = true;
            var telemetryLevel = LogEventLevel.Error;
            try
            {
                var configPath = Constants.ConfigFilePath;
                if (File.Exists(configPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                    if (doc.RootElement.TryGetProperty("logMinimumLevel", out var lvlProp))
                        Enum.TryParse(lvlProp.GetString(), ignoreCase: true, out minLevel);
                    if (doc.RootElement.TryGetProperty("telemetryEnabled", out var telProp)
                        && telProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        telemetryEnabled = telProp.GetBoolean();
                    if (doc.RootElement.TryGetProperty("telemetryMinimumLevel", out var telLvlProp))
                        Enum.TryParse(telLvlProp.GetString(), ignoreCase: true, out telemetryLevel);
                }
            }
            catch { /* use defaults */ }

            var configuration = new LoggerConfiguration()
                .MinimumLevel.Is(minLevel)
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: Constants.LogMaxFileSize,
                    retainedFileCountLimit: Constants.LogMaxFiles,
                    // Flush the file sink's buffer every 250ms so a UI-thread hang
                    // cannot swallow the last breadcrumb before the process dies.
                    flushToDiskInterval: TimeSpan.FromMilliseconds(250),
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}");

            if (telemetryEnabled)
            {
                // Anonymous error upload to the product site's admin portal. Independent minimum
                // level (default Error): the local file stays verbose while only real failures
                // leave the machine. Serilog disposes the sink on Shutdown; its final flush is
                // capped at a few seconds so a dead endpoint cannot hold the host open.
                var installId = TelemetryIdentity.GetOrCreateInstallId();
                configuration.WriteTo.Sink(
                    new TelemetrySink(new TelemetryClient(installId)),
                    restrictedToMinimumLevel: telemetryLevel);
            }

            Log.Logger = configuration.CreateLogger();

            Log.Information("AKML SQL {Version} logger initialized", Constants.RuntimeVersion);
        }

        public static void Shutdown()
        {
            Log.CloseAndFlush();
            Interlocked.Exchange(ref _initialized, 0);
        }
    }
}
