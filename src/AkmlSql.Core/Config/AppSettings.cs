using System;
using System.Collections.Generic;

namespace AkmlSql.Core.Config
{
    public class AppSettings
    {
        public int ConfigVersion { get; set; } = 1;
        public bool AutoUpdateEnabled { get; set; } = true;
        public bool TelemetryEnabled { get; set; }
        public DateTimeOffset? LastUpdateCheck { get; set; }
        public string InstallId { get; set; } = Guid.NewGuid().ToString();
        public List<InstalledTarget> InstalledTargets { get; set; } = new List<InstalledTarget>();
        public IntelliSenseSettings IntelliSense { get; set; } = new IntelliSenseSettings();
        public CacheSettings Cache { get; set; } = new CacheSettings();

        /// <summary>
        /// T093-T095: Whether the user has been prompted about native IntelliSense conflict.
        /// </summary>
        public bool NativeIntelliSensePrompted { get; set; }

        /// <summary>
        /// T093-T095: Whether AKML SQL disabled native SSMS IntelliSense (for restore on uninstall).
        /// </summary>
        public bool DisabledNativeIntelliSense { get; set; }
    }

    public enum KeywordCaseOption
    {
        Upper,
        Lower,
        PascalCase,
        AsIs
    }

    public class IntelliSenseSettings
    {
        public bool Enabled { get; set; } = true;
        public bool AutoTrigger { get; set; } = true;
        public int TriggerDelayMs { get; set; } = 100;
        public bool AfterDot { get; set; } = true;
        public int MaxSuggestions { get; set; } = 50;
        public bool FuzzyMatch { get; set; } = true;
        public bool ShowDataTypes { get; set; } = true;
        public bool ShowNullability { get; set; } = true;
        public bool ShowPkFk { get; set; } = true;
        public bool AutoAlias { get; set; } = true;
        public bool JoinAssist { get; set; } = true;
        public KeywordCaseOption KeywordCase { get; set; } = KeywordCaseOption.Upper;
        public bool DisableNativeIntelliSense { get; set; } = true;
    }

    public class CacheSettings
    {
        public bool AutoRefresh { get; set; } = true;
        public int RefreshIntervalSeconds { get; set; } = 300;
        public bool DetectDDL { get; set; } = true;
        public int MaxDatabases { get; set; } = 10;
        public bool LazyLoadColumns { get; set; } = true;
        public bool PersistToDisk { get; set; } = true;
        public string PersistPath { get; set; } = string.Empty;
    }

    public class InstalledTarget
    {
        public string IdeType { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string ExtensionsPath { get; set; } = string.Empty;
        public DateTimeOffset InstalledAt { get; set; }
    }
}
