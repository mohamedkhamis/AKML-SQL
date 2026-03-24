#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Represents a point-in-time snapshot of all open tabs in an SSMS/VS session.
    /// Persisted as JSON in <c>%AppData%\AKML SQL\sessions\</c> for crash recovery.
    /// </summary>
    public class SessionSnapshot
    {
        /// <summary>Unique identifier for this session (GUID string).</summary>
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>UTC timestamp when this snapshot was captured.</summary>
        [JsonPropertyName("capturedAt")]
        public DateTime CapturedAt { get; set; }

        /// <summary>Process ID of the SSMS/VS instance that created this snapshot.</summary>
        [JsonPropertyName("ssmsProcessId")]
        public int SsmsProcessId { get; set; }

        /// <summary>
        /// When <c>true</c>, this snapshot was taken during a clean shutdown.
        /// Crash recovery only considers sessions where this is <c>false</c>.
        /// </summary>
        [JsonPropertyName("isNormalShutdown")]
        public bool IsNormalShutdown { get; set; }

        /// <summary>The tabs that were open at the time of capture.</summary>
        [JsonPropertyName("tabs")]
        public List<TabSnapshot> Tabs { get; set; } = new();
    }

    /// <summary>
    /// Snapshot of a single open editor tab. Contains enough information to restore
    /// the tab content and reconnect to the original server/database.
    /// No passwords or secrets are stored — only server name, database, and auth type.
    /// </summary>
    public class TabSnapshot
    {
        /// <summary>Zero-based index of the tab in the document well.</summary>
        [JsonPropertyName("tabIndex")]
        public int TabIndex { get; set; }

        /// <summary>Display title of the tab (e.g. "SQLQuery1.sql").</summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Full text content of the editor at capture time.</summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>Absolute file path if the tab was saved to disk.</summary>
        [JsonPropertyName("filePath")]
        public string? FilePath { get; set; }

        /// <summary>SQL Server instance name (e.g. "localhost", "PROD-SQL01").</summary>
        [JsonPropertyName("server")]
        public string? Server { get; set; }

        /// <summary>Database name the tab was connected to.</summary>
        [JsonPropertyName("database")]
        public string? Database { get; set; }

        /// <summary>Authentication type (e.g. "Windows", "SqlAuth", "AzureAD"). No passwords.</summary>
        [JsonPropertyName("authType")]
        public string? AuthType { get; set; }

        /// <summary>Zero-based cursor line position.</summary>
        [JsonPropertyName("cursorLine")]
        public int CursorLine { get; set; }

        /// <summary>Zero-based cursor column position.</summary>
        [JsonPropertyName("cursorColumn")]
        public int CursorColumn { get; set; }

        /// <summary>Whether the tab was pinned in the document well.</summary>
        [JsonPropertyName("isPinned")]
        public bool IsPinned { get; set; }
    }
}
