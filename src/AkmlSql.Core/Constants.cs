using System;

namespace AkmlSql.Core
{
    public static class Constants
    {
        public const string ProductName = "AKML SQL";

        /// <summary>
        /// Compile-time version string. Used only where a const is required
        /// (e.g. <c>[InstalledProductRegistration]</c> attributes).
        /// For runtime version display, use <see cref="AppVersion.Current"/> instead.
        /// </summary>
        public const string Version = "1.0.0";

        /// <summary>
        /// Runtime version computed from git commit count + build timestamp.
        /// Format: 1.{commitCount}.{MMddHHmm}  (e.g. "1.265.04051456")
        /// </summary>
        public static string RuntimeVersion => AppVersion.Current;

        public const string BuildDate = "2026-03-17";
        public const string FeedbackUrl = "https://github.com/AkmlSql/feedback";
        public const string UpdateManifestUrl = "https://updates.akmlsql.com/manifest.json";

        public const string AppDataFolderName = "AKML SQL";
        public const string ConfigFileName = "config.json";
        public const string UpdateResultFileName = "update-available.json";
        public const string LogsFolderName = "logs";
        public const string CacheFolderName = "cache";

        public const int FormatDocumentCommandId = 0x0200;
        public const int FormatSelectionCommandId = 0x0201;

        public const int LogMaxFiles = 10;
        public const long LogMaxFileSize = 5 * 1024 * 1024; // 5 MB
        public const int UpdateCheckIntervalHours = 24;

        public static string AppDataPath =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppDataFolderName);

        public static string LocalAppDataPath =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppDataFolderName);

        public static string ConfigFilePath =>
            System.IO.Path.Combine(AppDataPath, ConfigFileName);

        public static string UpdateResultFilePath =>
            System.IO.Path.Combine(AppDataPath, UpdateResultFileName);

        public static string LogsPath =>
            System.IO.Path.Combine(AppDataPath, LogsFolderName);

        public static string CachePath =>
            System.IO.Path.Combine(LocalAppDataPath, CacheFolderName);
    }
}
