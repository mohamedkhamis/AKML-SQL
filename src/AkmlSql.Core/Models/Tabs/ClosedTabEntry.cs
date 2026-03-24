#nullable enable
using System;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Represents a recently closed editor tab whose content can be restored.
    /// Captured when a document is closed in the IDE and stored in the
    /// <c>ClosedTabStack</c> for later restoration.
    /// </summary>
    public sealed class ClosedTabEntry
    {
        /// <summary>The full SQL text content of the tab at the time it was closed.</summary>
        public string Content { get; }

        /// <summary>
        /// The file path of the document, if it was saved to disk.
        /// <c>null</c> for unsaved / untitled query windows.
        /// </summary>
        public string? FilePath { get; }

        /// <summary>
        /// The SQL Server instance name the tab was connected to, if available.
        /// </summary>
        public string? Server { get; }

        /// <summary>
        /// The database context the tab was using, if available.
        /// </summary>
        public string? Database { get; }

        /// <summary>
        /// The authentication type (e.g. "Windows", "SQL Server"), if available.
        /// </summary>
        public string? AuthType { get; }

        /// <summary>
        /// UTC timestamp of when the tab was closed.
        /// </summary>
        public DateTime ClosedAt { get; }

        /// <summary>
        /// The display title of the tab at the time it was closed (e.g. "SQLQuery1.sql").
        /// </summary>
        public string? TabTitle { get; }

        /// <summary>Creates an immutable <see cref="ClosedTabEntry"/> instance.</summary>
        public ClosedTabEntry(
            string content,
            string? filePath,
            string? server,
            string? database,
            string? authType,
            DateTime closedAt,
            string? tabTitle)
        {
            Content  = content ?? throw new ArgumentNullException(nameof(content));
            FilePath = filePath;
            Server   = server;
            Database = database;
            AuthType = authType;
            ClosedAt = closedAt.Kind == DateTimeKind.Utc ? closedAt : closedAt.ToUniversalTime();
            TabTitle = tabTitle;
        }
    }
}
