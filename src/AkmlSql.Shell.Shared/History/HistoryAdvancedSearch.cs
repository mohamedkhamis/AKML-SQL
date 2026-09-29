#nullable enable
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (T134, HIS-07) — the Advanced search panel under SQL History's search box: a period
    /// (preset or custom dates), a server and a database, Starred and Open. Kept in settings only
    /// when "Remember advanced search settings" is on.
    /// </summary>
    internal sealed class HistoryAdvancedSearch : INotifyPropertyChanged
    {
        internal static readonly (string Value, string Label)[] Periods =
        {
            ("all", "Everything"),
            ("week", "Last week"),
            ("month", "Last month"),
            ("3months", "Last 3 months"),
            ("custom", "Custom"),
        };

        private string _period = "all";
        private DateTime? _from, _to;
        private string? _server, _database;
        private bool _starred, _openOnly;

        public string Period { get => _period; set => Set(ref _period, string.IsNullOrEmpty(value) ? "all" : value); }
        public DateTime? From { get => _from; set => Set(ref _from, value); }
        public DateTime? To { get => _to; set => Set(ref _to, value); }
        public string? Server { get => _server; set => Set(ref _server, string.IsNullOrEmpty(value) ? null : value); }
        public string? Database { get => _database; set => Set(ref _database, string.IsNullOrEmpty(value) ? null : value); }
        public bool Starred { get => _starred; set => Set(ref _starred, value); }
        public bool OpenOnly { get => _openOnly; set => Set(ref _openOnly, value); }

        public bool IsCustom => _period == "custom";

        /// <summary>The period as a date range (local): presets reach back from <paramref name="now"/>; custom covers whole days.</summary>
        internal (DateTime? From, DateTime? To) Range(DateTime now)
        {
            switch (_period)
            {
                case "week": return (now.AddDays(-7), null);
                case "month": return (now.AddMonths(-1), null);
                case "3months": return (now.AddMonths(-3), null);
                case "custom":
                    return (_from.HasValue ? DateTime.SpecifyKind(_from.Value.Date, DateTimeKind.Local) : (DateTime?)null,
                            _to.HasValue ? DateTime.SpecifyKind(_to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local) : (DateTime?)null);
                default: return (null, null);
            }
        }

        internal string PeriodLabel()
        {
            if (_period == "custom")
                return $"{_from?.ToString("d", CultureInfo.CurrentCulture) ?? "…"} – {_to?.ToString("d", CultureInfo.CurrentCulture) ?? "…"}";
            foreach (var (value, label) in Periods)
                if (value == _period) return label;
            return "Everything";
        }

        public void Reset()
        {
            Period = "all";
            From = To = null;
            Server = Database = null;
            Starred = OpenOnly = false;
        }

        internal HistoryAdvancedSearchState ToState() => new HistoryAdvancedSearchState
        {
            Period = _period,
            From = _from?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = _to?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Server = _server,
            Database = _database,
            Starred = _starred,
            OpenOnly = _openOnly,
        };

        internal void Load(HistoryAdvancedSearchState state)
        {
            Period = state.Period;
            From = ParseDay(state.From);
            To = ParseDay(state.To);
            Server = state.Server;
            Database = state.Database;
            Starred = state.Starred;
            OpenOnly = state.OpenOnly;
        }

        private static DateTime? ParseDay(string? text) =>
            DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name == nameof(Period)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCustom)));
        }
    }

    /// <summary>Spec 040 (HIS-07) — one active filter shown as a chip under the search box, with a remove button.</summary>
    internal sealed class HistoryFilterChip
    {
        public HistoryFilterChip(string kind, string label, ICommand remove)
        {
            Kind = kind;
            Label = label;
            Remove = remove;
        }

        /// <summary>Which filter: period, server, database, starred or open.</summary>
        public string Kind { get; }
        public string Label { get; }
        public ICommand Remove { get; }
    }
}
