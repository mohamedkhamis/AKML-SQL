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
