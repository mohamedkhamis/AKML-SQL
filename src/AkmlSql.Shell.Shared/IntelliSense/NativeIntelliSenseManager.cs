using System;
using System.Windows.Forms;
using AkmlSql.Core;
using AkmlSql.Core.Config;
using Serilog;

namespace AkmlSql.Shell.Shared.IntelliSense
{
    /// <summary>
    /// T093-T095: Manages conflict resolution with SSMS's built-in IntelliSense.
    /// Detects native IntelliSense state, prompts the user on first load, and
    /// persists the choice in config.
    ///
    /// Shell code: .NET Framework 4.7.2, C# 7.3 compatible.
    /// </summary>
    public sealed class NativeIntelliSenseManager
    {
        private static NativeIntelliSenseManager _instance;
        private static readonly object _lock = new object();
        private bool _initialized;

        private NativeIntelliSenseManager() { }

        public static NativeIntelliSenseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new NativeIntelliSenseManager();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Checks whether native SSMS IntelliSense appears to be enabled.
        /// SSMS stores this setting in the registry under the SSMS hive.
        /// </summary>
        public bool IsNativeIntelliSenseEnabled()
        {
            try
            {
                // SSMS IntelliSense is controlled via:
                // HKCU\Software\Microsoft\SQL Server Management Studio\XX.0\Settings\IntelliSense
                // Key: EnableIntelliSense (DWORD, 1 = enabled)
                // We check common versions
                string[] registryPaths = new string[]
                {
                    @"Software\Microsoft\SQL Server Management Studio\20.0\Settings\IntelliSense",
                    @"Software\Microsoft\SQL Server Management Studio\22.0\Settings\IntelliSense",
                    @"Software\Microsoft\SSMS\22.0\Settings\IntelliSense"
                };

                foreach (var path in registryPaths)
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            var value = key.GetValue("EnableIntelliSense");
                            if (value is int intVal && intVal == 1)
                                return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to detect native IntelliSense state");
                return false;
            }
        }

        /// <summary>
        /// Shows a one-time dialog on first load asking the user whether to disable
        /// SSMS's native IntelliSense to avoid conflicts. Persists the choice.
        /// </summary>
        public void CheckAndPromptOnFirstLoad()
        {
            if (_initialized)
                return;
            _initialized = true;

            try
            {
                var config = ConfigManager.Load();
                if (config == null)
                    return;

                // Check if we've already prompted the user
                // The NativeIntelliSensePrompted flag is stored in config
                if (config.NativeIntelliSensePrompted)
                {
                    Log.Debug("NativeIntelliSenseManager: user already prompted, skipping.");
                    return;
                }

                if (!IsNativeIntelliSenseEnabled())
                {
                    // Native IntelliSense not detected, mark as prompted
                    config.NativeIntelliSensePrompted = true;
                    ConfigManager.Save(config);
                    return;
                }

                // Show dialog asking to disable native IntelliSense
                var result = MessageBox.Show(
                    "SSMS's built-in IntelliSense is currently enabled. " +
                    "AKML SQL provides enhanced IntelliSense that may conflict with the native feature.\n\n" +
                    "Would you like to disable SSMS's native IntelliSense?\n\n" +
                    "(You can re-enable it later from SSMS Tools > Options > Text Editor > Transact-SQL > IntelliSense)",
                    Constants.ProductName + " - IntelliSense Conflict",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                config.NativeIntelliSensePrompted = true;

                if (result == DialogResult.Yes)
                {
                    DisableNativeIntelliSense();
                    config.DisabledNativeIntelliSense = true;
                    Log.Information("User chose to disable native SSMS IntelliSense");
                }
                else
                {
                    config.DisabledNativeIntelliSense = false;
                    Log.Information("User chose to keep native SSMS IntelliSense");
                }

                ConfigManager.Save(config);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to check/prompt for native IntelliSense conflict");
            }
        }

        /// <summary>
        /// Attempts to disable SSMS native IntelliSense via registry.
        /// </summary>
        private void DisableNativeIntelliSense()
        {
            string[] registryPaths = new string[]
            {
                @"Software\Microsoft\SQL Server Management Studio\20.0\Settings\IntelliSense",
                @"Software\Microsoft\SQL Server Management Studio\22.0\Settings\IntelliSense",
                @"Software\Microsoft\SSMS\22.0\Settings\IntelliSense"
            };

            foreach (var path in registryPaths)
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path, writable: true))
                    {
                        if (key != null)
                        {
                            key.SetValue("EnableIntelliSense", 0, Microsoft.Win32.RegistryValueKind.DWord);
                            Log.Information("Disabled native IntelliSense at {Path}", path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to disable native IntelliSense at {Path}", path);
                }
            }
        }

        /// <summary>
        /// T096: Re-enables native IntelliSense. Called during uninstall to restore original state.
        /// </summary>
        public void RestoreNativeIntelliSense()
        {
            try
            {
                var config = ConfigManager.Load();
                if (config == null || !config.DisabledNativeIntelliSense)
                    return;

                string[] registryPaths = new string[]
                {
                    @"Software\Microsoft\SQL Server Management Studio\20.0\Settings\IntelliSense",
                    @"Software\Microsoft\SQL Server Management Studio\22.0\Settings\IntelliSense",
                    @"Software\Microsoft\SSMS\22.0\Settings\IntelliSense"
                };

                foreach (var path in registryPaths)
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path, writable: true))
                    {
                        if (key != null)
                        {
                            key.SetValue("EnableIntelliSense", 1, Microsoft.Win32.RegistryValueKind.DWord);
                            Log.Information("Restored native IntelliSense at {Path}", path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to restore native IntelliSense");
            }
        }
    }
}
