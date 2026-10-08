using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AkmlSql.Core.Models.Tabs
{
    /// <summary>
    /// Spec 040 (OPT-08, FR-054, data-model §1.2) — a named tab-colour environment (SQL Prompt's
    /// model): colouring rules pick an environment by <see cref="Name"/> and take its
    /// <see cref="Color"/>. Stored under <c>tabs.environments</c> in <c>config.json</c>.
    /// </summary>
    public sealed class TabEnvironment
    {
        /// <summary>Unique (case-insensitive), 1–40 characters. Rules reference it by this name.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary><c>#RRGGBB</c>.</summary>
        [JsonPropertyName("color")]
        public string Color { get; set; } = string.Empty;

        /// <summary>A detached copy, for editors that work on a copy until OK.</summary>
        public TabEnvironment Clone() => new TabEnvironment { Name = Name, Color = Color };

        /// <summary>
        /// The four stock environments, in the order the stock rules use them: PRODUCTION
        /// <c>#FF4444</c>, STAGING <c>#FFB800</c>, DEV <c>#44BB44</c> and AZURE <c>#4488FF</c>.
        /// A new list on every call, so callers may change it.
        /// </summary>
        public static List<TabEnvironment> CreateDefaults() => new List<TabEnvironment>
        {
            new TabEnvironment { Name = "PRODUCTION", Color = "#FF4444" },
            new TabEnvironment { Name = "STAGING", Color = "#FFB800" },
            new TabEnvironment { Name = "DEV", Color = "#44BB44" },
            new TabEnvironment { Name = "AZURE", Color = "#4488FF" },
        };
    }
}
