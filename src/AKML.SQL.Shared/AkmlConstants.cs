using System;
using System.IO;

namespace AKML.SQL.Shared;

/// <summary>
/// Shared constants used across all AKML-SQL components.
/// </summary>
public static class AkmlConstants
{
    /// <summary>
    /// Product information
    /// </summary>
    public const string ProductName = "AKML-SQL";
    public const string ProductVersion = "1.0.0";
    public const string CompanyName = "Azka Innovation";
    
    /// <summary>
    /// Named pipe configuration for IPC
    /// </summary>
    public const string PipeName = "akml-sql-bridge";
    public const string PipeAddress = @"\\.\pipe\" + PipeName;
    
    /// <summary>
    /// Default port for gRPC (fallback if named pipes fail)
    /// </summary>
    public const int DefaultGrpcPort = 50051;
    
    /// <summary>
    /// Core service process name
    /// </summary>
    public const string CoreProcessName = "AKML.SQL.Core";
    public const string CoreExecutableName = "AKML.SQL.Core.exe";
    
    /// <summary>
    /// Timeouts (in milliseconds)
    /// </summary>
    public static class Timeouts
    {
        public const int ProcessStart = 10000;      // 10 seconds to start Core
        public const int GrpcConnection = 5000;     // 5 seconds to connect
        public const int GrpcCall = 30000;          // 30 seconds per call
        public const int TextSyncDebounce = 300;    // 300ms debounce for text sync
        public const int CompletionDebounce = 150;  // 150ms debounce for completions
        public const int MetadataCache = 300000;    // 5 minutes metadata cache
    }
    
    /// <summary>
    /// File paths and directories
    /// </summary>
    public static class Paths
    {
        public static string AppDataFolder => 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductName);
        
        public static string LogFolder => Path.Combine(AppDataFolder, "Logs");
        public static string CacheFolder => Path.Combine(AppDataFolder, "Cache");
        public static string SettingsFolder => Path.Combine(AppDataFolder, "Settings");
        
        public static string SettingsFile => Path.Combine(SettingsFolder, "settings.json");
        public static string SnippetsFile => Path.Combine(SettingsFolder, "snippets.json");
        public static string StylesFile => Path.Combine(SettingsFolder, "styles.json");
    }
    
    /// <summary>
    /// SSMS content types for text classification
    /// </summary>
    public static class ContentTypes
    {
        public const string SqlServerTools = "SQL Server Tools";
        public const string Sql = "sql";
        public const string TSql = "T-SQL";
    }
    
    /// <summary>
    /// Logging categories
    /// </summary>
    public static class LogCategories
    {
        public const string Core = "AKML.SQL.Core";
        public const string SSMS = "AKML.SQL.SSMS";
        public const string IPC = "AKML.SQL.IPC";
        public const string Parser = "AKML.SQL.Parser";
        public const string Completion = "AKML.SQL.Completion";
        public const string Metadata = "AKML.SQL.Metadata";
    }
}
