using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 030 T052 — the analysis rule catalog returned to the shell's Manage Rules dialog.
    /// Sent Engine -> Shell as MessageType 133 (ListAnalysisRulesResult). Pairs with request 33.
    /// </summary>
    [MessagePackObject]
    public class ListAnalysisRulesResponse
    {
        /// <summary>Whether the catalog was built successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>One entry per discovered rule, sorted by rule id.</summary>
        [Key(1)]
        public AnalysisRuleInfoDto[] Rules { get; set; } = [];

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(2)]
        public string? Error { get; set; }
    }

    /// <summary>
    /// A single rule row for the Manage Rules dialog: identity, category, the resolved
    /// enabled/severity state, schema dependency, auto-fix availability, and display text.
    /// </summary>
    [MessagePackObject]
    public class AnalysisRuleInfoDto
    {
        /// <summary>Stable rule id (e.g. "PE001").</summary>
        [Key(0)]
        public string RuleId { get; set; } = string.Empty;

        /// <summary>Short human-readable name (e.g. "SELECT * in procedures/views").</summary>
        [Key(1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Category name (e.g. "Performance", "Security").</summary>
        [Key(2)]
        public string Category { get; set; } = string.Empty;

        /// <summary>The rule's built-in default severity, as a <c>DiagnosticSeverity</c> int.</summary>
        [Key(3)]
        public int DefaultSeverity { get; set; }

        /// <summary>The severity actually in effect after global + .casettings overrides, as an int.</summary>
        [Key(4)]
        public int EffectiveSeverity { get; set; }

        /// <summary>Whether the rule is currently enabled (after overrides).</summary>
        [Key(5)]
        public bool Enabled { get; set; }

        /// <summary>True when the rule only runs with a live schema cache (skipped otherwise).</summary>
        [Key(6)]
        public bool RequiresSchema { get; set; }

        /// <summary>True when the rule ships a deterministic auto-fix (drives lightbulb colour, T054).</summary>
        [Key(7)]
        public bool AutoFixable { get; set; }

        /// <summary>One-line description for the detail pane / tooltip.</summary>
        [Key(8)]
        public string Description { get; set; } = string.Empty;
    }
}
