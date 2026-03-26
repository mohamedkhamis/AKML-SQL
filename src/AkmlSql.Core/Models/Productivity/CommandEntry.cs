#nullable enable
using System;

namespace AkmlSql.Core.Models.Productivity
{
    /// <summary>An item in the Command Palette.</summary>
    public class CommandEntry
    {
        /// <summary>Stable command identifier (e.g., "akml.formatDocument").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Display name (e.g., "Format SQL Document").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Group: Format, Analysis, History, Refactoring, Navigation, Settings, SSMS.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Keyboard shortcut display hint (e.g., "Ctrl+K, Y").</summary>
        public string? KeyboardShortcut { get; set; }

        /// <summary>Times invoked via Command Palette (for frequency ranking).</summary>
        public int UsageCount { get; set; }

        /// <summary>Last invocation timestamp (for recency ranking).</summary>
        public DateTime? LastUsed { get; set; }
    }
}
