#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 040 (T104, STY-08) — the style list behind the Active Style menu. Menus read it
    /// synchronously while they open, so it is a snapshot refreshed in the background: your own
    /// styles first, then the built-ins, each A→Z, with the one named by
    /// <c>Formatter.ActiveProfile</c> marked active. A failed or disconnected refresh keeps the
    /// last snapshot.
    /// </summary>
    internal sealed class ActiveStyleCache
    {
        /// <summary>Menus ask on every open; the engine is asked at most this often.</summary>
        internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

        public static ActiveStyleCache Instance { get; } =
            new ActiveStyleCache(EngineRpcClientAccessor.Instance, () => DateTime.UtcNow);

        private static readonly IReadOnlyList<(string Name, string Source, bool IsActive)> None =
            new (string, string, bool)[0];

        private readonly IRpcClientAccessor _rpc;
        private readonly Func<DateTime> _clock;
        private readonly object _gate = new object();
        private IReadOnlyList<(string Name, string Source, bool IsActive)> _styles = None;
        private DateTime? _lastRefreshUtc;
        private Task _pending = Task.CompletedTask;

        internal ActiveStyleCache(IRpcClientAccessor rpc, Func<DateTime> clock)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>The last snapshot; empty until the first refresh completes.</summary>
        public IReadOnlyList<(string Name, string Source, bool IsActive)> Styles => Volatile.Read(ref _styles);

        /// <summary>Raised after each refresh that produced a snapshot, and after <see cref="MarkActive"/>.</summary>
        public event EventHandler? Changed;

        /// <summary>Refreshes in the background unless one started less than <see cref="RefreshInterval"/> ago.</summary>
        public void RequestRefresh()
        {
            lock (_gate)
            {
                var now = _clock();
                if (_lastRefreshUtc is DateTime last && now - last < RefreshInterval) return;
                _lastRefreshUtc = now;
            }
            Start();
        }

        /// <summary>Refreshes in the background now — after the user changed the styles or the active one.</summary>
        public void RefreshNow()
        {
            lock (_gate) _lastRefreshUtc = _clock();
            Start();
        }

        private void Start()
        {
            var task = RefreshAsync();
            lock (_gate) _pending = task;
        }

        /// <summary>Test seam: completes when the last background refresh has.</summary>
        internal Task WhenIdleAsync()
        {
            lock (_gate) return _pending;
        }

        /// <summary>Asks the engine for the style list and marks the active one from config.</summary>
        public async Task RefreshAsync()
        {
            if (!_rpc.IsConnected) return;

            ProfileListResponse? response;
            try
            {
                response = await _rpc.SendRequestAsync<ProfileListResponse, ProfileListRequest>(
                    MessageTypes.ProfileList, new ProfileListRequest(), timeoutMs: 3000).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Active style list: refresh failed; keeping the last list");
                return;
            }
            if (response?.Profiles == null) return;

            string? active;
            try { active = ConfigManager.Load().Formatter.ActiveProfile; }
            catch (Exception ex)
            {
                Log.Debug(ex, "Active style list: could not read the active style");
                active = null;
            }

            var styles = response.Profiles
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.IsBuiltIn)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => (p.Name, p.IsBuiltIn ? "Built-in" : "Yours",
                              string.Equals(p.Name, active, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            Volatile.Write(ref _styles, styles);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Marks <paramref name="name"/> active in the snapshot at once, before the engine confirms it.</summary>
        public void MarkActive(string name)
        {
            var styles = Styles
                .Select(s => (s.Name, s.Source, string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            Volatile.Write(ref _styles, styles);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
