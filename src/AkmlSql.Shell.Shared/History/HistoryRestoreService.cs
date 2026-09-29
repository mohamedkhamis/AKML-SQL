#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (T142, HIS-14) — restore on start: reopens the queries that were open when SSMS last
    /// closed or crashed (<see cref="HistoryRestoreState.RestorableEntryIds"/>, from the startup
    /// reconcile), as the History settings say:
    /// <list type="bullet">
    /// <item><c>Tabs.SessionRecovery</c> turns it on or off;</item>
    /// <item><c>Tabs.RestoreOnStartup</c>: "always" reopens them, "prompt" asks which, "never" doesn't;</item>
    /// <item><c>History.RestoreMaxQueries</c> caps how many (newest first);</item>
    /// <item><c>History.ReconnectRestoredQueries</c> connects each to its server and database.</item>
    /// </list>
    /// The opener and the prompt are injectable for tests.
    /// </summary>
    internal sealed class HistoryRestoreService
    {
        internal enum RestoreMode { Always, Prompt, Never }

        private readonly IRpcClientAccessor _rpc;

        public HistoryRestoreService() : this(EngineRpcClientAccessor.Instance) { }

        internal HistoryRestoreService(IRpcClientAccessor rpc)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        }

        internal Func<AppSettings> SettingsProvider { get; set; } = ConfigManager.Load;

        /// <summary>Opens one restored query; the flag says whether to connect it to its server and database.</summary>
        internal Action<HistoryEntryDto, bool> Opener { get; set; } = (entry, reconnect) =>
            HistoryQueryOpener.OpenInNewTab(entry.SqlText, reconnect ? entry.Server : null, reconnect ? entry.Database : null,
                entry.SessionKey, "History.sql");

        /// <summary>Asks which queries to restore; returns the chosen ones (none for "Not now").</summary>
        internal Func<IReadOnlyList<HistoryEntryDto>, IReadOnlyList<HistoryEntryDto>> Prompt { get; set; } =
            RestoreQueriesDialog.Choose;

        internal static RestoreMode ModeOf(string? value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "always": return RestoreMode.Always;
                case "never": return RestoreMode.Never;
                default: return RestoreMode.Prompt;
            }
        }

        /// <summary>Reopens the queries as the settings say; returns how many were opened.</summary>
        public async Task<int> RestoreAsync(IReadOnlyList<long> entryIds)
        {
            AppSettings settings;
            try { settings = SettingsProvider(); }
            catch (Exception ex)
            {
                Log.Debug(ex, "History restore: settings unavailable");
                return 0;
            }

            if (!settings.Tabs.SessionRecovery) return 0;
            var mode = ModeOf(settings.Tabs.RestoreOnStartup);
            if (mode == RestoreMode.Never) return 0;

            var max = Math.Max(1, Math.Min(100, settings.History.RestoreMaxQueries));
            var ids = (entryIds ?? Array.Empty<long>()).Distinct().Take(max).ToArray();
            if (ids.Length == 0 || !_rpc.IsConnected) return 0;

            HistoryEntryDto[] entries;
            try
            {
                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction,
                    new HistoryActionRequest { Action = HistoryActions.GetEntries, EntryIds = ids },
                    timeoutMs: 10000);
                entries = (response?.Entries ?? Array.Empty<HistoryEntryDto>())
                    .Where(e => !string.IsNullOrWhiteSpace(e.SqlText))
                    .ToArray();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "History restore: could not read the queries");
                return 0;
            }
            if (entries.Length == 0) return 0;

            var chosen = mode == RestoreMode.Always ? entries : Prompt(entries) ?? Array.Empty<HistoryEntryDto>();
            var reconnect = settings.History.ReconnectRestoredQueries;
            var opened = 0;
            foreach (var entry in chosen)
            {
                try
                {
                    Opener(entry, reconnect);
                    opened++;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "History restore: could not reopen entry {Id}", entry.Id);
                }
            }

            Log.Information("History restore: reopened {Opened} of {Count} queries ({Mode})", opened, entries.Length, mode);
            return opened;
        }
    }
}
