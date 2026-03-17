using System.IO;
using System.Threading;
using Serilog;

namespace AkmlSql.Core.Logging
{
    public static class LoggerFactory
    {
        private static int _initialized;

        public static void Initialize()
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
                return;

            var logPath = Path.Combine(Constants.LogsPath, "akmlsql-.log");

            Directory.CreateDirectory(Constants.LogsPath);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: Constants.LogMaxFileSize,
                    retainedFileCountLimit: Constants.LogMaxFiles,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("AKML SQL {Version} logger initialized", Constants.Version);
        }

        public static void Shutdown()
        {
            Log.CloseAndFlush();
            Interlocked.Exchange(ref _initialized, 0);
        }
    }
}
