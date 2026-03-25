#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Config;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity
{
    /// <summary>
    /// T104: Manages connection aliases — friendly names for SQL Server instances.
    /// Loads alias mappings from <see cref="NavigationSettings.ConnectionAliases"/> in config.json.
    /// Provides <see cref="Resolve"/> to convert a raw server name to its alias.
    /// </summary>
    internal sealed class ConnectionAliasManager
    {
        private static ConnectionAliasManager? _instance;
        private readonly Dictionary<string, string> _aliasMap;

        /// <summary>
        /// Gets the singleton instance. Returns null if not yet initialized.
        /// </summary>
        public static ConnectionAliasManager? Instance => _instance;

        private ConnectionAliasManager(IReadOnlyList<ConnectionAliasEntry> aliases)
        {
            _aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in aliases)
            {
                if (!string.IsNullOrWhiteSpace(entry.ServerName) &&
                    !string.IsNullOrWhiteSpace(entry.Alias))
                {
                    _aliasMap[entry.ServerName.Trim()] = entry.Alias.Trim();
                }
            }

            Log.Debug("ConnectionAliasManager: loaded {Count} aliases", _aliasMap.Count);
        }

        /// <summary>
        /// Initializes the singleton from the current configuration.
        /// Safe to call multiple times — subsequent calls reload from config.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                var settings = ConfigManager.Load();
                var aliases = settings.Navigation?.ConnectionAliases
                              ?? new List<ConnectionAliasEntry>();

                _instance = new ConnectionAliasManager(aliases);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ConnectionAliasManager: failed to initialize");
                _instance = new ConnectionAliasManager(new List<ConnectionAliasEntry>());
            }
        }

        /// <summary>
        /// Reloads the alias mappings from the current config.
        /// </summary>
        public static void Reload()
        {
            Initialize();
        }

        /// <summary>
        /// Resolves a server name to its friendly alias.
        /// Returns the alias if found, otherwise returns the original server name unchanged.
        /// </summary>
        /// <param name="serverName">The raw SQL Server instance name.</param>
        /// <returns>The alias or the original server name.</returns>
        public string Resolve(string serverName)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                return serverName;

            if (_aliasMap.TryGetValue(serverName.Trim(), out var alias))
                return alias;

            // Try wildcard/pattern matching (simple glob: * at start/end)
            foreach (var kvp in _aliasMap)
            {
                if (MatchesPattern(serverName, kvp.Key))
                    return kvp.Value;
            }

            return serverName;
        }

        /// <summary>
        /// Returns all configured aliases as a readonly list.
        /// </summary>
        public IReadOnlyList<ConnectionAliasEntry> GetAll()
        {
            return _aliasMap.Select(kvp => new ConnectionAliasEntry
            {
                ServerName = kvp.Key,
                Alias = kvp.Value
            }).ToList();
        }

        /// <summary>
        /// Adds or updates an alias mapping.
        /// This only updates the in-memory map — caller must persist via ConfigManager.
        /// </summary>
        public void SetAlias(string serverName, string alias)
        {
            if (string.IsNullOrWhiteSpace(serverName)) return;

            if (string.IsNullOrWhiteSpace(alias))
            {
                _aliasMap.Remove(serverName.Trim());
                Log.Debug("ConnectionAliasManager: removed alias for {Server}", serverName);
            }
            else
            {
                _aliasMap[serverName.Trim()] = alias.Trim();
                Log.Debug("ConnectionAliasManager: set alias {Server} -> {Alias}", serverName, alias);
            }
        }

        /// <summary>
        /// Removes an alias mapping.
        /// </summary>
        public bool RemoveAlias(string serverName)
        {
            return _aliasMap.Remove(serverName.Trim());
        }

        /// <summary>
        /// Saves the current alias map back to config.
        /// </summary>
        public void SaveToConfig()
        {
            try
            {
                var settings = ConfigManager.Load();
                settings.Navigation.ConnectionAliases = _aliasMap.Select(kvp => new ConnectionAliasEntry
                {
                    ServerName = kvp.Key,
                    Alias = kvp.Value
                }).ToList();

                ConfigManager.Save(settings);
                Log.Information("ConnectionAliasManager: saved {Count} aliases to config", _aliasMap.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ConnectionAliasManager: failed to save aliases to config");
            }
        }

        /// <summary>
        /// Simple glob matching: "*" at start/end of pattern.
        /// </summary>
        private static bool MatchesPattern(string input, string pattern)
        {
            if (pattern.StartsWith("*") && pattern.EndsWith("*"))
            {
                var inner = pattern.Substring(1, pattern.Length - 2);
                return input.IndexOf(inner, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (pattern.StartsWith("*"))
            {
                var suffix = pattern.Substring(1);
                return input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }

            if (pattern.EndsWith("*"))
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                return input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
