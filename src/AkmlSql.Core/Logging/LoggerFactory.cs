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

            // Read minimum log level from config JSON directly (avoids circular dependency with ConfigManager)
            var minLevel = LogEventLevel.Debug;
            try
            {
                var configPath = Constants.ConfigFilePath;
                if (File.Exists(configPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                    if (doc.RootElement.TryGetProperty("logMinimumLevel", out var lvlProp))
                        Enum.TryParse(lvlProp.GetString(), ignoreCase: true, out minLevel);
                }
            }
            catch { /* use default */ }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(minLevel)
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: Constants.LogMaxFileSize,
                    retainedFileCountLimit: Constants.LogMaxFiles,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("AKML SQL {Version} logger initialized", Constants.RuntimeVersion);
        }

        public static void Shutdown()
        {
            Log.CloseAndFlush();
            Interlocked.Exchange(ref _initialized, 0);
        }
    }
}
