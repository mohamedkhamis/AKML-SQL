#nullable enable
using System;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity
{
    /// <summary>
    /// T107: Initializes the connection alias subsystem during package startup.
    /// Loads aliases from config and wires integration with window title and status bar.
    /// </summary>
    internal static class ConnectionAliasInitializer
    {
        private static bool _initialized;

        /// <summary>
        /// Initializes the connection alias manager and its integrations.
        /// Must be called on the UI thread during package initialization.
        /// </summary>
        public static void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_initialized) return;

            try
            {
                ConnectionAliasManager.Initialize();
                ConnectionAliasIntegration.Initialize();

                _initialized = true;
                Log.Information("ConnectionAliasInitializer: initialized");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ConnectionAliasInitializer: failed to initialize");
            }
        }

        /// <summary>
        /// Initializes for SSMS 20 (synchronous Package).
        /// </summary>
        public static void InitializeSync()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Initialize();
        }
    }
}
