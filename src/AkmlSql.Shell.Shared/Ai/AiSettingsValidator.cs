using AkmlSql.Core.Config;
using Serilog;
using System;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// Validates AI settings on package initialization and logs the current configuration state.
    /// Called from each AkmlSqlPackage during non-critical initialization.
    /// </summary>
    internal static class AiSettingsValidator
    {
        public static void Initialize()
        {
            try
            {
                var settings = ConfigManager.Load();
                if (settings.Ai.Enabled)
                {
                    if (string.IsNullOrEmpty(settings.Ai.Provider))
                        Log.Warning("AI is enabled but no provider is configured");
                    else
                        Log.Information("AI assistant enabled: provider={Provider}, model={Model}, privacy={Privacy}",
                            settings.Ai.Provider, settings.Ai.Model, settings.Ai.PrivacyMode);
                }
                else
                {
                    Log.Debug("AI assistant is disabled");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to validate AI settings");
            }
        }
    }
}
