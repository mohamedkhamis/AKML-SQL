namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Domain model representing a tab-coloring environment rule. Maps to
    /// <see cref="Config.ColoringRule"/> from <c>config.json</c> but serves as the
    /// runtime representation used by the shell-side environment detector.
    /// </summary>
    public sealed class EnvironmentRule
    {
        /// <summary>
        /// Sort order — rules are evaluated lowest-first; the first match wins.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Comma-separated glob patterns (e.g. <c>"*PROD*,*LIVE*"</c>).
        /// Supports <c>*</c> wildcard at the start and/or end of each sub-pattern.
        /// Matching is case-insensitive.
        /// </summary>
        public string Pattern { get; }

        /// <summary>
        /// Which connection property to match against. Currently only <c>"serverName"</c>
        /// is supported; future versions may support <c>"databaseName"</c>, <c>"userName"</c>, etc.
        /// </summary>
        public string MatchTarget { get; }

        /// <summary>
        /// Background colour for the document tab header, as a hex string (e.g. <c>"#FF4444"</c>).
        /// </summary>
        public string Color { get; }

        /// <summary>
        /// Human-readable environment label displayed on the tab (e.g. <c>"PRODUCTION"</c>).
        /// </summary>
        public string Label { get; }

        /// <summary>Creates an immutable <see cref="EnvironmentRule"/> instance.</summary>
        public EnvironmentRule(int order, string? pattern, string? matchTarget, string? color, string? label)
        {
            Order       = order;
            Pattern     = pattern ?? string.Empty;
            MatchTarget = matchTarget ?? "serverName";
            Color       = color ?? string.Empty;
            Label       = label ?? string.Empty;
        }
    }
}
