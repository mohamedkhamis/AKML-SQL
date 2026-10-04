#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Serilog;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// MVVM ViewModel for the SQL History tool window.
    /// Manages filter state, search execution, pagination, action commands
    /// (copy, open in new tab, re-execute, compare, toggle favorite, delete, export),
    /// and the observable collection of history entries displayed in the WPF list view.
    /// </summary>
    internal class HistoryViewModel : INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private string? _selectedServer;
        private string? _selectedDatabase;
        private int? _selectedStatus;
        private DateTime? _dateFrom;
        private DateTime? _dateTo;
        private bool _favoritesOnly;
        private bool? _isOpenFilter;
        private bool _isDisconnected;
        private int _totalCount;
        private bool _isLoading;
        private int _currentOffset;
        private HistoryEntryDto? _selectedEntry;
        private const int PageSize = 100;

        // Spec 040 (HIS-03): rows the last page returned. "More" needs a full last page AND fewer
        // rows than the total — the old rule added the offset to the already-accumulated list,
        // so it counted every earlier page twice and stopped after page two.
        private int _lastPageCount;
        private bool? _hasMore;

        // Spec 040 (HIS-01): full text per entry for the preview, cleared on every refresh.
        private readonly Dictionary<long, string> _previewCache = new Dictionary<long, string>();

        private readonly IRpcClientAccessor _rpc;

        /// <summary>
        /// Raised when a "Compare" action completes with the two sides for the side-by-side diff
        /// (spec 040, HIS-10: each side carries a name and a time for its header).
        /// </summary>
        internal event Action<HistoryCompareSide, HistoryCompareSide>? CompareRequested;

        /// <summary>
        /// Raised when an "Open query" action completes with the full SQL text.
        /// The event handler receives the SQL text, server, database and the entry's session key
        /// (spec 040: the new document adopts it, so running it again continues the session).
        /// </summary>
        internal event Action<string, string?, string?, string?>? OpenInNewTabRequested;

        /// <summary>
        /// Raised when a "Re-execute" action completes: the SQL text and the server and database it
        /// ran on (spec 040, HIS-12: re-execute uses the entry's connection).
        /// </summary>
        internal event Action<string, string?, string?>? ReExecuteRequested;

        public HistoryViewModel() : this(EngineRpcClientAccessor.Instance) { }

        /// <summary>Spec 040: the engine client is injectable so paging and row actions are testable.</summary>
        internal HistoryViewModel(IRpcClientAccessor rpc)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            Entries = new ObservableCollection<HistoryEntryDto>();
            SelectedEntries = new ObservableCollection<HistoryEntryDto>();
            Servers = new ObservableCollection<string>();
            Databases = new ObservableCollection<string>();

            // Search/filter commands
            SearchCommand = new RelayCommand(_ => ExecuteSearchAsync(), _ => !IsLoading);
            ClearFiltersCommand = new RelayCommand(_ => ExecuteClearFiltersAsync(), _ => !IsLoading);
            LoadMoreCommand = new RelayCommand(_ => ExecuteLoadMoreAsync(), _ => !IsLoading && HasMoreEntries);

            // US3 action commands
            // Spec 040 (HIS-11): a row menu passes its row; otherwise the selected entry.
            CopySqlCommand = new RelayCommand(p => ExecuteCopySqlAsync(p as HistoryEntryDto ?? SelectedEntry),
                p => !IsLoading && (p is HistoryEntryDto || SelectedEntry != null));
            OpenInNewTabCommand = new RelayCommand(p => ExecuteOpenInNewTabAsync(p as HistoryEntryDto ?? SelectedEntry),
                p => !IsLoading && (p is HistoryEntryDto || SelectedEntry != null));
            ReExecuteCommand = new RelayCommand(p => ExecuteReExecuteAsync(p as HistoryEntryDto ?? SelectedEntry),
                p => !IsLoading && (p is HistoryEntryDto || SelectedEntry != null));
            // Spec 040 (HIS-12): "Compare…" on a row compares the two selected rows, or the row's
            // previous version with its current text.
            CompareCommand = new RelayCommand(p => ExecuteCompareAsync(p as HistoryEntryDto),
                p => !IsLoading && (p is HistoryEntryDto || SelectedEntries.Count == 2 || SelectedEntry != null));

            // US9 action commands. Spec 040 (HIS-04): a row's star / delete pass that row as the
            // command parameter; without one, the selected entry.
            ToggleFavoriteCommand = new RelayCommand(p => ExecuteToggleFavoriteAsync(p as HistoryEntryDto ?? SelectedEntry),
                p => !IsLoading && (p is HistoryEntryDto || SelectedEntry != null));
            DeleteCommand = new RelayCommand(p => ExecuteDeleteAsync(DeleteTargets(p as HistoryEntryDto)),
                p => !IsLoading && (p is HistoryEntryDto || SelectedEntry != null));
            RemoveOlderThanCommand = new RelayCommand(p => ExecuteRemoveOlderThanAsync(p as HistoryEntryDto ?? SelectedEntry),
                p => !IsLoading && (p is HistoryEntryDto || SelectedEntry != null));
            ExportCommand = new RelayCommand(_ => ExecuteExportAsync(), _ => !IsLoading);

            // Spec 040 (HIS-10/HIS-11/HIS-12)
            RenameCommand = new RelayCommand(p => _ = RenameEntryAsync(p as HistoryEntryDto ?? SelectedEntry),
                p => !IsLoading && (p is HistoryEntryDto || SelectedEntry != null));
            ClearHistoryCommand = new RelayCommand(_ => _ = ClearHistoryAsync(), _ => !IsLoading);
            CompareWithCurrentCommand = new RelayCommand(p => _ = CompareWithCurrentAsync(SelectedEntry, p as HistoryVersionDto),
                p => !IsLoading && SelectedEntry != null && p is HistoryVersionDto);
            OpenDocument = (sql, server, database, key) => OpenInNewTabRequested?.Invoke(sql, server, database, key);
            Execute = (sql, server, database) => ReExecuteRequested?.Invoke(sql, server, database);
            ShowCompare = (left, right) => CompareRequested?.Invoke(left, right);

            // Spec 040 (HIS-07/HIS-09)
            AdvancedSearch = new HistoryAdvancedSearch();
            ActiveFilterChips = new ObservableCollection<HistoryFilterChip>();
            RetryCommand = new RelayCommand(_ => ExecuteRetryAsync());
        }

        #region Spec 040 — search as you type, Advanced search, live refresh, reconnect

        /// <summary>
        /// Test seam: runs <c>action</c> once after <c>delay</c>; disposing the result cancels it.
        /// Production uses a one-shot DispatcherTimer (callers are on the UI thread).
        /// </summary>
        internal Func<TimeSpan, Action, IDisposable> Scheduler { get; set; } = DispatcherSchedule;

        /// <summary>Test seam: the local time Advanced search periods count back from.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.Now;

        internal static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);
        internal static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(5);

        private IDisposable? _pendingSearch;
        private IDisposable? _probe;
        private bool _suppressSearchDelay;

        /// <summary>The Advanced search panel's filters.</summary>
        public HistoryAdvancedSearch AdvancedSearch { get; }

        /// <summary>The active filters as removable chips under the search box.</summary>
        public ObservableCollection<HistoryFilterChip> ActiveFilterChips { get; }

        /// <summary>"Retry" on the disconnected overlay: searches again at once.</summary>
        public ICommand RetryCommand { get; }

        private static IDisposable DispatcherSchedule(TimeSpan delay, Action action)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
            return new StopOnDispose(timer);
        }

        private sealed class StopOnDispose : IDisposable
        {
            private readonly System.Windows.Threading.DispatcherTimer _timer;
            public StopOnDispose(System.Windows.Threading.DispatcherTimer timer) => _timer = timer;
            public void Dispose() => _timer.Stop();
        }

        /// <summary>Typing searches 250 ms after the last change (one search, not one per key).</summary>
        private void ScheduleSearch()
        {
            if (_suppressSearchDelay) return;
            _pendingSearch?.Dispose();
            _pendingSearch = Scheduler(SearchDelay, () =>
            {
                _pendingSearch = null;
                _ = RunSearchAsync(resetOffset: true);
            });
        }

        /// <summary>Enter in the search box: search now, dropping any delayed search.</summary>
        internal void SearchNow()
        {
            _pendingSearch?.Dispose();
            _pendingSearch = null;
            _ = RunSearchAsync(resetOffset: true);
        }

        /// <summary>Sets a filter property without starting a delayed search (the caller searches).</summary>
        private void Quietly(Action change)
        {
            _suppressSearchDelay = true;
            try { change(); }
            finally { _suppressSearchDelay = false; }
        }

        /// <summary>While the engine is down, checks every 5 s and reloads once it is back.</summary>
        private void StartProbe()
        {
            if (_probe != null) return;
            _probe = Scheduler(ProbeInterval, () =>
            {
                _probe = null;
                if (_rpc.IsConnected) _ = RunSearchAsync(resetOffset: true);
                else StartProbe();
            });
        }

        private void StopProbe()
        {
            _probe?.Dispose();
            _probe = null;
        }

        private async void ExecuteRetryAsync()
        {
            try
            {
                StopProbe();
                await RunSearchAsync(resetOffset: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: retry failed");
            }
        }

        private IDisposable? _pendingRefresh;

        /// <summary>
        /// A run or draft reached History (ExecutionCapture.HistoryRecorded, marshalled to the UI
        /// thread): refresh once, shortly after the last of a burst, keeping the selection.
        /// </summary>
        internal void OnHistoryRecorded()
        {
            _pendingRefresh?.Dispose();
            _pendingRefresh = Scheduler(TimeSpan.FromMilliseconds(300), () =>
            {
                _pendingRefresh = null;
                _ = HandleHistoryRecordedAsync();
            });
        }

        /// <summary>Re-queries the first page, keeping the selected query (by id, else by its session).</summary>
        internal Task HandleHistoryRecordedAsync() => RefreshKeepingSelectionAsync();

        private HistoryEntryDto? _keepSelection;

        internal async Task RefreshKeepingSelectionAsync()
        {
            _keepSelection = SelectedEntry;
            try { await SearchInternalAsync(resetOffset: true); }
            finally { _keepSelection = null; }
        }

        /// <summary>The Advanced search filters, applied: they become the search's filters, then it runs.</summary>
        internal async Task ApplyAdvancedSearchAsync()
        {
            var (from, to) = AdvancedSearch.Range(Clock());
            Quietly(() =>
            {
                DateFrom = from;
                DateTo = to;
                SelectedServer = AdvancedSearch.Server;
                SelectedDatabase = AdvancedSearch.Database;
                FavoritesOnly = AdvancedSearch.Starred;
                IsOpenFilter = AdvancedSearch.OpenOnly ? true : (bool?)null;
            });
            RebuildChips();
            SaveAdvancedSearchIfRemembered();
            await SearchInternalAsync(resetOffset: true);
        }

        private void RebuildChips()
        {
            ActiveFilterChips.Clear();
            void Add(string kind, string label) =>
                ActiveFilterChips.Add(new HistoryFilterChip(kind, label, new RelayCommand(p => _ = RemoveChipAsync((HistoryFilterChip)p!))));

            if (AdvancedSearch.Period != "all") Add("period", AdvancedSearch.PeriodLabel());
            if (AdvancedSearch.Server != null) Add("server", "Server: " + AdvancedSearch.Server);
            if (AdvancedSearch.Database != null) Add("database", "Database: " + AdvancedSearch.Database);
            if (AdvancedSearch.Starred) Add("starred", "Starred");
            if (AdvancedSearch.OpenOnly) Add("open", "Open");
        }

        /// <summary>Removing a chip clears just that filter and searches again.</summary>
        internal Task RemoveChipAsync(HistoryFilterChip chip)
        {
            switch (chip.Kind)
            {
                case "period": AdvancedSearch.Period = "all"; AdvancedSearch.From = AdvancedSearch.To = null; break;
                case "server": AdvancedSearch.Server = null; break;
                case "database": AdvancedSearch.Database = null; break;
                case "starred": AdvancedSearch.Starred = false; break;
                case "open": AdvancedSearch.OpenOnly = false; break;
            }
            return ApplyAdvancedSearchAsync();
        }

        private void SaveAdvancedSearchIfRemembered()
        {
            try
            {
                if (!SettingsProvider().History.RememberAdvancedSearch) return;
                var settings = Core.Config.ConfigManager.Load();
                settings.History.AdvancedSearch = AdvancedSearch.ToState();
                Core.Config.ConfigManager.Save(settings);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "HistoryViewModel: could not save the advanced search settings");
            }
        }

        /// <summary>Starts from the remembered Advanced search filters, when remembering is on.</summary>
        internal void LoadRememberedAdvancedSearch()
        {
            try
            {
                var history = SettingsProvider().History;
                if (!history.RememberAdvancedSearch || history.AdvancedSearch == null) return;
                AdvancedSearch.Load(history.AdvancedSearch);
                var (from, to) = AdvancedSearch.Range(Clock());
                Quietly(() =>
                {
                    DateFrom = from;
                    DateTo = to;
                    SelectedServer = AdvancedSearch.Server;
                    SelectedDatabase = AdvancedSearch.Database;
                    FavoritesOnly = AdvancedSearch.Starred;
                    IsOpenFilter = AdvancedSearch.OpenOnly ? true : (bool?)null;
                });
                RebuildChips();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "HistoryViewModel: could not load the advanced search settings");
            }
        }

        /// <summary>Fills the server and database lists for the Advanced search panel from all of History.</summary>
        internal async Task LoadFilterValuesAsync()
        {
            if (!_rpc.IsConnected) return;
            try
            {
                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction, new HistoryActionRequest { Action = HistoryActions.GetFilterValues }, timeoutMs: 5000);
                if (response?.Success != true) return;
                Servers.Clear();
                foreach (var s in response.Servers ?? Array.Empty<string>()) Servers.Add(s);
                Databases.Clear();
                foreach (var d in response.Databases ?? Array.Empty<string>()) Databases.Add(d);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "HistoryViewModel: could not load the filter values");
            }
        }

        #endregion

        #region Spec 040 — version-aware row actions and their seams (HIS-10/HIS-11/HIS-12)

        private HistoryVersionDto? _selectedVersion;

        /// <summary>
        /// The earlier version picked in the versions pane, for <see cref="SelectedEntry"/>; null when
        /// the current text is meant. Open, Copy SQL and Re-execute use it.
        /// </summary>
        public HistoryVersionDto? SelectedVersion
        {
            get => _selectedVersion;
            set => SetField(ref _selectedVersion, value);
        }

        /// <summary>Opens text in a new query tab: SQL, server, database, session key.</summary>
        internal Action<string, string?, string?, string?> OpenDocument { get; set; }

        /// <summary>Switches to the open document with this full name; false when it can't.</summary>
        internal Func<string, bool> ActivateDocument { get; set; } = _ => false;

        /// <summary>The full name of the open document holding a session key, or null.</summary>
        internal Func<string, string?> FindOpenDocument { get; set; } = DocumentSessionKeys.TryFindDocument;

        internal Action<string> SetClipboard { get; set; } = text => Clipboard.SetText(text);

        /// <summary>Runs text against a server and database (Re-execute).</summary>
        internal Action<string, string?, string?> Execute { get; set; }

        internal Action<HistoryCompareSide, HistoryCompareSide> ShowCompare { get; set; }

        /// <summary>A short message for the status bar.</summary>
        internal Action<string> Notify { get; set; } = text => StatusBar.StatusBarManager.ShowTransient(text, 4);

        /// <summary>Asks for a query's new name (given the current one); null when cancelled.</summary>
        internal Func<string, string?> PromptRename { get; set; } = _ => null;

        /// <summary>"Rename query" on the row menu and F2.</summary>
        public ICommand RenameCommand { get; }

        /// <summary>The toolbar's "Clear history…": removes everything except starred queries.</summary>
        public ICommand ClearHistoryCommand { get; }

        /// <summary>"Compare with current" on a version row (the parameter).</summary>
        public ICommand CompareWithCurrentCommand { get; }

        /// <summary>The selected earlier version when <paramref name="entry"/> is the selected row.</summary>
        private HistoryVersionDto? VersionFor(HistoryEntryDto entry) =>
            _selectedVersion != null && ReferenceEquals(entry, _selectedEntry) ? _selectedVersion : null;

        /// <summary>The text an action uses: the selected version's, else the entry's full text.</summary>
        private async Task<(string? Text, string? Server, string? Database)> ActionTextAsync(HistoryEntryDto entry)
        {
            var version = VersionFor(entry);
            if (version != null)
                return (version.SqlText, version.Server ?? entry.Server, version.Database ?? entry.Database);
            return (await GetFullSqlAsync(entry.Id), entry.Server, entry.Database);
        }

        /// <summary>
        /// Open query (row menu, Open button, Enter). A query open in a tab here, with no earlier
        /// version picked, switches to that tab; anything else opens in a new tab.
        /// </summary>
        internal async Task OpenEntryAsync(HistoryEntryDto entry)
        {
            if (entry == null) return;
            if (VersionFor(entry) == null && !string.IsNullOrEmpty(entry.SessionKey))
            {
                var document = FindOpenDocument(entry.SessionKey!);
                if (document != null && ActivateDocument(document)) return;
            }

            var (text, server, database) = await ActionTextAsync(entry);
            if (text != null) OpenDocument(text, server, database, entry.SessionKey);
        }

        internal async Task CopyEntryAsync(HistoryEntryDto entry)
        {
            if (entry == null) return;
            var (text, _, _) = await ActionTextAsync(entry);
            if (text == null) return;
            try { SetClipboard(text); }
            catch (System.Runtime.InteropServices.ExternalException ex) { Log.Debug(ex, "HistoryViewModel: clipboard busy"); }
        }

        internal async Task ReExecuteEntryAsync(HistoryEntryDto entry)
        {
            if (entry == null) return;
            var (text, server, database) = await ActionTextAsync(entry);
            if (text != null) Execute(text, server, database);
        }

        /// <summary>"Compare with current": an earlier version against the query's current text.</summary>
        internal async Task CompareWithCurrentAsync(HistoryEntryDto? entry, HistoryVersionDto? version)
        {
            if (entry == null || version == null) return;
            var current = await GetFullSqlAsync(entry.Id);
            if (current == null) return;
            var name = HistoryRowDisplay.DisplayNameFor(entry);
            ShowCompare(new HistoryCompareSide(name, version.SavedAt, version.SqlText),
                        new HistoryCompareSide(name + " (current)", entry.ExecutedAt, current));
        }

        /// <summary>True when the query is open in a tab (here or in another SSMS).</summary>
        internal bool IsOpenInTab(HistoryEntryDto entry) =>
            entry.IsOpen || (!string.IsNullOrEmpty(entry.SessionKey) && FindOpenDocument(entry.SessionKey!) != null);

        /// <summary>
        /// Rename query. Refused while the query is open: an open query takes its tab's name, so a
        /// new name would be replaced at the next run.
        /// </summary>
        internal async Task<bool> RenameEntryAsync(HistoryEntryDto? entry)
        {
            if (entry == null) return false;
            var current = HistoryRowDisplay.DisplayNameFor(entry);
            if (IsOpenInTab(entry))
            {
                Notify($"'{current}' is open in a tab. Close it to rename the query.");
                return false;
            }

            string? newName;
            try
            {
                newName = PromptRename(current)?.Trim();
            }
            catch (Exception ex)
            {
                // the command runs this fire-and-forget: say so, or a failed prompt is invisible
                Log.Warning(ex, "HistoryViewModel: the rename prompt failed");
                return false;
            }
            if (string.IsNullOrEmpty(newName) || newName == current || !_rpc.IsConnected) return false;

            try
            {
                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction,
                    new HistoryActionRequest { Action = HistoryActions.Rename, EntryIds = new[] { entry.Id }, NewName = newName },
                    timeoutMs: 5000);
                if (response?.Success != true)
                {
                    Log.Warning("HistoryViewModel: rename failed: {Error}", response?.Error);
                    return false;
                }
                entry.TabTitle = newName;
                await RefreshKeepingSelectionAsync();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: rename failed");
                return false;
            }
        }

        /// <summary>"Clear history…": after confirmation, removes every query except starred ones.</summary>
        internal async Task ClearHistoryAsync()
        {
            if (!_rpc.IsConnected) return;
            if (!ConfirmPrompt("Remove all queries except starred ones? This can't be undone.")) return;
            try
            {
                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction, new HistoryActionRequest { Action = HistoryActions.DeleteAll }, timeoutMs: 30000);
                if (response?.Success == true) await SearchInternalAsync(resetOffset: true);
                else Log.Warning("HistoryViewModel: clear history failed: {Error}", response?.Error);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: clear history failed");
            }
        }

        #endregion

        #region Properties

        /// <summary>Full-text search query.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                // Spec 040 (HIS-07): search as you type.
                if (SetField(ref _searchText, value)) ScheduleSearch();
            }
        }

        /// <summary>Selected server filter (null = all servers).</summary>
        public string? SelectedServer
        {
            get => _selectedServer;
            set => SetField(ref _selectedServer, value);
        }

        /// <summary>Selected database filter (null = all databases).</summary>
        public string? SelectedDatabase
        {
            get => _selectedDatabase;
            set => SetField(ref _selectedDatabase, value);
        }

        /// <summary>Selected status filter (null = all statuses).</summary>
        public int? SelectedStatus
        {
            get => _selectedStatus;
            set => SetField(ref _selectedStatus, value);
        }

        /// <summary>Date range lower bound (null = no lower bound).</summary>
        public DateTime? DateFrom
        {
            get => _dateFrom;
            set => SetField(ref _dateFrom, value);
        }

        /// <summary>Date range upper bound (null = no upper bound).</summary>
        public DateTime? DateTo
        {
            get => _dateTo;
            set => SetField(ref _dateTo, value);
        }

        /// <summary>When true, only show favorite entries.</summary>
        public bool FavoritesOnly
        {
            get => _favoritesOnly;
            set => SetField(ref _favoritesOnly, value);
        }

        /// <summary>Filter by open/closed tab status. Null = all, true = open, false = closed.</summary>
        public bool? IsOpenFilter
        {
            get => _isOpenFilter;
            set => SetField(ref _isOpenFilter, value);
        }

        /// <summary>
        /// True when the last search could not run because the out-of-process engine was not
        /// connected. Drives the "History unavailable" affordance so a pipe-down state is not
        /// silently indistinguishable from a genuinely empty result.
        /// </summary>
        public bool IsDisconnected
        {
            get => _isDisconnected;
            set => SetField(ref _isDisconnected, value);
        }

        /// <summary>Total number of matching entries across all pages.</summary>
        public int TotalCount
        {
            get => _totalCount;
            set
            {
                if (SetField(ref _totalCount, value))
                {
                    OnPropertyChanged(nameof(HasMoreEntries));
                }
            }
        }

        /// <summary>Whether a search request is currently in flight.</summary>
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetField(ref _isLoading, value))
                {
                    // Re-evaluate CanExecute on all commands
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Whether there are more entries available beyond the loaded ones.</summary>
        /// <remarks>
        /// The engine says (<see cref="HistorySearchResponse.HasMore"/>): a CamelCase search is
        /// filtered in memory, so its pages come back short while more matches follow.
        /// </remarks>
        public bool HasMoreEntries => _hasMore ?? (_lastPageCount == PageSize && Entries.Count < TotalCount);

        /// <summary>Spec 040 test seam: where settings come from (grouping on/off). Production reads config.</summary>
        internal Func<Core.Config.AppSettings> SettingsProvider { get; set; } = Core.Config.ConfigManager.Load;

        /// <summary>
        /// Spec 040 (HIS-04) seam: asks the user to confirm a destructive row action; true = go
        /// ahead. Defaults to a Yes/No message box.
        /// </summary>
        internal Func<string, bool> ConfirmPrompt { get; set; } = text =>
            MessageBox.Show(text, Core.Constants.ProductName, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        /// <summary>Count of entries in the current page that are marked as favorite.
        /// Bound to the star badge on the Starred filter tab. Refreshed after every search.</summary>
        public int StarredCount => Entries.Count(e => e.IsFavorite);

        /// <summary>Observable collection of history entries for the current page.</summary>
        public ObservableCollection<HistoryEntryDto> Entries { get; }

        /// <summary>Currently selected single entry (for single-selection actions).</summary>
        public HistoryEntryDto? SelectedEntry
        {
            get => _selectedEntry;
            set
            {
                if (SetField(ref _selectedEntry, value))
                {
                    SelectedVersion = null; // a version belongs to the row it was picked on
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// Currently selected entries (for multi-selection actions like Compare and Delete).
        /// Updated by the UI control when selection changes.
        /// </summary>
        public ObservableCollection<HistoryEntryDto> SelectedEntries { get; }

        /// <summary>Distinct server names for the filter dropdown.</summary>
        public ObservableCollection<string> Servers { get; }

        /// <summary>Distinct database names for the filter dropdown.</summary>
        public ObservableCollection<string> Databases { get; }

        #endregion

        #region Commands

        /// <summary>Executes a search with current filter values, replacing existing results.</summary>
        public ICommand SearchCommand { get; }

        /// <summary>Resets all filters and re-executes the search.</summary>
        public ICommand ClearFiltersCommand { get; }

        /// <summary>Loads the next page of results, appending to existing entries.</summary>
        public ICommand LoadMoreCommand { get; }

        /// <summary>Copies the full SQL text of the selected entry to clipboard.</summary>
        public ICommand CopySqlCommand { get; }

        /// <summary>Opens the full SQL text of the selected entry in a new editor tab.</summary>
        public ICommand OpenInNewTabCommand { get; }

        /// <summary>Re-executes the selected entry's SQL in the active connection.</summary>
        public ICommand ReExecuteCommand { get; }

        /// <summary>Compares two selected entries side-by-side (requires exactly 2 selections).</summary>
        public ICommand CompareCommand { get; }

        /// <summary>Toggles the favorite flag on the selected entry.</summary>
        public ICommand ToggleFavoriteCommand { get; }

        /// <summary>Deletes the selected entries from history.</summary>
        public ICommand DeleteCommand { get; }

        /// <summary>Spec 030 T074 — removes all history older than the selected entry (favorites kept).</summary>
        public ICommand RemoveOlderThanCommand { get; }

        /// <summary>Exports history entries to a file (CSV, JSON, or SQL).</summary>
        public ICommand ExportCommand { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initializes the ViewModel by loading distinct servers/databases for dropdowns
        /// and performing an initial search.
        /// </summary>
        public async void InitializeAsync()
        {
            try
            {
                await LoadFilterDropdownsAsync();
                LoadRememberedAdvancedSearch(); // spec 040 (HIS-07)
                await SearchInternalAsync(resetOffset: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: initialization failed");
            }
        }

        /// <summary>
        /// Called by the UI when the ListView selection changes to update SelectedEntries.
        /// </summary>
        internal void UpdateSelectedEntries(System.Collections.IList selectedItems)
        {
            SelectedEntries.Clear();
            if (selectedItems != null)
            {
                foreach (var item in selectedItems)
                {
                    if (item is HistoryEntryDto dto)
                    {
                        SelectedEntries.Add(dto);
                    }
                }
            }

            // Update SelectedEntry to the first selected item
            SelectedEntry = SelectedEntries.Count > 0 ? SelectedEntries[0] : null;
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Cycles the open/closed tab filter behind the toolbar's two folder toggles and
        /// re-runs the search. <paramref name="open"/> is the button that was clicked
        /// (true = "open only", false = "closed only"). Clicking the button for the state
        /// that is already active clears the filter back to "all"; clicking the other button
        /// switches straight to it. Open and closed are mutually exclusive — mirrors the
        /// Redgate SQL History behaviour (report §3 rec #1).
        /// </summary>
        public void ToggleOpenFilter(bool open)
        {
            // A search is in flight — mutating the filter now would paint the toggle active
            // without re-running the search (active-looking filter over an unfiltered list).
            // Ignore the click, like a disabled button.
            if (IsLoading) return;

            IsOpenFilter = (IsOpenFilter == open) ? (bool?)null : open;
            if (SearchCommand.CanExecute(null))
                SearchCommand.Execute(null);
        }

        #endregion

        #region Search Methods

        private async void ExecuteSearchAsync()
        {
            try
            {
                await SearchInternalAsync(resetOffset: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: search failed");
            }
        }

        private async void ExecuteClearFiltersAsync()
        {
            try
            {
                Quietly(() =>
                {
                    SearchText = string.Empty;
                    SelectedServer = null;
                    SelectedDatabase = null;
                    SelectedStatus = null;
                    DateFrom = null;
                    DateTo = null;
                    FavoritesOnly = false;
                    IsOpenFilter = null;
                    AdvancedSearch.Reset();
                });
                RebuildChips();
                _pendingSearch?.Dispose();

                await SearchInternalAsync(resetOffset: true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: clear filters failed");
            }
        }

        private async void ExecuteLoadMoreAsync()
        {
            try
            {
                await SearchInternalAsync(resetOffset: false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: load more failed");
            }
        }

        /// <summary>Runs a search now (first page, or the next page) and completes when it has.</summary>
        internal Task RunSearchAsync(bool resetOffset) => SearchInternalAsync(resetOffset);

        private async Task SearchInternalAsync(bool resetOffset)
        {
            if (!_rpc.IsConnected)
            {
                Log.Debug("HistoryViewModel: engine not connected, cannot search");
                IsDisconnected = true;
                StartProbe();
                return;
            }

            StopProbe();
            IsDisconnected = false;
            IsLoading = true;

            try
            {
                if (resetOffset)
                {
                    _currentOffset = 0;
                    _previewCache.Clear();
                }
                else
                {
                    _currentOffset += PageSize;
                }

                // Read deduplication preference from config
                var settings = SettingsProvider();
                var deduplicate = settings.History.Deduplication;

                // Parse advanced search syntax (prefix filters, wildcards, etc.)
                var parsed = HistorySearchParser.Parse(SearchText);

                // If the search text contains prefix filters, apply them to override the UI filter fields
                var effectiveServer = SelectedServer;
                var effectiveDatabase = SelectedDatabase;
                var effectiveFavoritesOnly = FavoritesOnly;
                var effectiveSearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

                if (parsed.HasPrefixes)
                {
                    if (parsed.ServerFilter != null) effectiveServer = parsed.ServerFilter;
                    if (parsed.DatabaseFilter != null) effectiveDatabase = parsed.DatabaseFilter;
                    if (parsed.StarredFilter.HasValue) effectiveFavoritesOnly = parsed.StarredFilter.Value;
                    // Use the SQL filter as FTS5 query if specified, otherwise use the plain text query
                    effectiveSearchText = !string.IsNullOrWhiteSpace(parsed.SqlFilter)
                        ? parsed.SqlFilter
                        : (!string.IsNullOrWhiteSpace(parsed.PlainTextQuery) ? parsed.PlainTextQuery : null);
                }
                else if (parsed.CamelCaseTokens?.Count > 0)
                {
                    // CamelCase tokens (e.g. "PC") were extracted and will be applied as an in-memory
                    // post-filter. Do NOT pass them to FTS5 as a literal query — FTS5 would return no
                    // results for "PC" when the actual matches are "ProductCategory", "price_calc", etc.
                    // Use PlainTextQuery (the non-CamelCase remainder) for FTS5 so that broad results
                    // are returned and the post-filter narrows them down correctly.
                    effectiveSearchText = string.IsNullOrWhiteSpace(parsed.PlainTextQuery)
                        ? null
                        : parsed.PlainTextQuery.Trim();
                }

                // Determine effective open/closed filter from tab or search prefix
                bool? effectiveIsOpen = IsOpenFilter;
                if (parsed.HasPrefixes && parsed.OpenFilter.HasValue)
                    effectiveIsOpen = parsed.OpenFilter.Value;

                // Extract name filter from parsed search prefixes
                string? effectiveNameFilter = null;
                if (parsed.HasPrefixes && parsed.NameFilter != null)
                    effectiveNameFilter = parsed.NameFilter;

                // Spec 040 (HIS-07): path: and date:[… TO …] from the search box.
                var effectiveDateFrom = parsed.DateFrom ?? DateFrom;
                var effectiveDateTo = parsed.DateTo ?? DateTo;

                var request = new HistorySearchRequest
                {
                    SearchText = effectiveSearchText,
                    Server = effectiveServer,
                    Database = effectiveDatabase,
                    Status = SelectedStatus,
                    DateFrom = effectiveDateFrom?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    DateTo = effectiveDateTo?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    FavoritesOnly = effectiveFavoritesOnly,
                    Deduplicate = deduplicate,
                    Offset = _currentOffset,
                    Limit = PageSize,
                    IsOpen = effectiveIsOpen,
                    NameFilter = effectiveNameFilter,
                    CamelCaseTokens = parsed.CamelCaseTokens?.ToArray(),
                    PathFilter = parsed.PathFilter,
                    // sql: means the SQL text only, as its help says.
                    SqlOnly = parsed.HasPrefixes && !string.IsNullOrWhiteSpace(parsed.SqlFilter),
                };

                var response = await _rpc.SendRequestAsync<HistorySearchResponse, HistorySearchRequest>(
                    MessageTypes.HistorySearch, request, timeoutMs: 10000);

                if (response.Success)
                {
                    if (resetOffset)
                    {
                        Entries.Clear();
                    }

                    var page = response.Entries ?? Array.Empty<HistoryEntryDto>();
                    foreach (var entry in page)
                    {
                        Entries.Add(entry);
                    }

                    _lastPageCount = page.Length;
                    _hasMore = response.HasMore;
                    TotalCount = response.TotalCount;
                    OnPropertyChanged(nameof(HasMoreEntries));
                    OnPropertyChanged(nameof(StarredCount));

                    // Spec 040 (HIS-09): a new search selects its first row; a refresh keeps the
                    // selected query (by id, or by its session when a new run replaced the row).
                    if (resetOffset)
                    {
                        var keep = _keepSelection;
                        SelectedEntry = keep == null
                            ? Entries.FirstOrDefault()
                            : Entries.FirstOrDefault(e => e.Id == keep.Id)
                              ?? (string.IsNullOrEmpty(keep.SessionKey) ? null : Entries.FirstOrDefault(e => e.SessionKey == keep.SessionKey))
                              ?? Entries.FirstOrDefault();
                    }
                }
                else
                {
                    Log.Warning("HistoryViewModel: search returned error: {Error}", response.Error);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadFilterDropdownsAsync()
        {
            await Task.CompletedTask; // Placeholder — dropdowns are populated from search results
        }

        /// <summary>
        /// Updates the filter dropdown lists based on current search results.
        /// Called after each successful search to keep dropdowns current.
        /// </summary>
        internal void RefreshDropdownsFromEntries()
        {
            var serverSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dbSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Preserve existing items
            foreach (var s in Servers) serverSet.Add(s);
            foreach (var d in Databases) dbSet.Add(d);

            // Add from current entries
            foreach (var entry in Entries)
            {
                if (!string.IsNullOrEmpty(entry.Server)) serverSet.Add(entry.Server);
                if (!string.IsNullOrEmpty(entry.Database)) dbSet.Add(entry.Database);
            }

            // Only update if changed
            if (serverSet.Count != Servers.Count)
            {
                Servers.Clear();
                foreach (var s in serverSet) Servers.Add(s);
            }

            if (dbSet.Count != Databases.Count)
            {
                Databases.Clear();
                foreach (var d in dbSet) Databases.Add(d);
            }
        }

        #endregion

        #region Action Command Implementations (US3)

        /// <summary>
        /// Sends a GetFullSql action to the engine, retrieves the full SQL text,
        /// and copies it to the clipboard.
        /// </summary>
        private async void ExecuteCopySqlAsync(HistoryEntryDto? entry)
        {
            try
            {
                if (entry != null) await CopyEntryAsync(entry);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: copy SQL failed");
            }
        }

        /// <summary>
        /// Sends a GetFullSql action to the engine and raises OpenInNewTabRequested
        /// so the UI control can open the SQL in a new editor tab via DTE.
        /// </summary>
        private async void ExecuteOpenInNewTabAsync(HistoryEntryDto? entry)
        {
            try
            {
                if (entry != null) await OpenEntryAsync(entry);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: open in new tab failed");
            }
        }

        /// <summary>
        /// Sends a GetFullSql action to the engine and raises ReExecuteRequested
        /// so the UI control can paste the SQL into the active editor and execute.
        /// </summary>
        private async void ExecuteReExecuteAsync(HistoryEntryDto? entry)
        {
            try
            {
                if (entry != null) await ReExecuteEntryAsync(entry);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: re-execute failed");
            }
        }

        /// <summary>
        /// Sends a GetDiff action with two entry IDs and raises CompareRequested
        /// with the two SQL texts for side-by-side comparison.
        /// </summary>
        private async void ExecuteCompareAsync(HistoryEntryDto? row)
        {
            try
            {
                if (!_rpc.IsConnected) return;

                // One row (or a row outside a two-row selection): its previous version against its current text.
                if (SelectedEntries.Count != 2 || (row != null && !SelectedEntries.Contains(row)))
                {
                    await ComparePreviousVersionAsync(row ?? SelectedEntry);
                    return;
                }

                var first = SelectedEntries[0];
                var second = SelectedEntries[1];
                var id1 = first.Id;
                var id2 = second.Id;

                IsLoading = true;
                try
                {
                    var actionRequest = new HistoryActionRequest
                    {
                        Action = HistoryActions.GetDiff,
                        EntryIds = new[] { id1, id2 }
                    };

                    var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                        MessageTypes.HistoryAction, actionRequest, timeoutMs: 10000);

                    if (response.Success && response.DiffLeftSql != null && response.DiffRightSql != null)
                    {
                        ShowCompare(new HistoryCompareSide(HistoryRowDisplay.DisplayNameFor(first), first.ExecutedAt, response.DiffLeftSql),
                                    new HistoryCompareSide(HistoryRowDisplay.DisplayNameFor(second), second.ExecutedAt, response.DiffRightSql));
                    }
                    else
                    {
                        Log.Warning("HistoryViewModel: compare returned error: {Error}", response.Error);
                    }
                }
                finally
                {
                    IsLoading = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: compare failed");
            }
        }

        /// <summary>Compares a query's newest earlier version with its current text.</summary>
        private async Task ComparePreviousVersionAsync(HistoryEntryDto? entry)
        {
            if (entry == null) return;
            var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                MessageTypes.HistoryAction,
                new HistoryActionRequest { Action = HistoryActions.GetVersions, EntryIds = new[] { entry.Id }, GroupScope = true },
                timeoutMs: 10000);
            var versions = response?.Versions ?? Array.Empty<HistoryVersionDto>();
            if (response?.Success != true || versions.Length < 2)
            {
                Notify("This query has no earlier versions to compare.");
                return;
            }
            var name = HistoryRowDisplay.DisplayNameFor(entry);
            ShowCompare(new HistoryCompareSide(name, versions[1].SavedAt, versions[1].SqlText),
                        new HistoryCompareSide(name + " (current)", versions[0].SavedAt, versions[0].SqlText));
        }

        /// <summary>
        /// Spec 040 (HIS-01, FR-010): the entry's complete text for the preview — the list rows
        /// carry only the first 500 characters. Cached per entry until the next refresh, and it
        /// does not raise <see cref="IsLoading"/>, so selecting a row never flashes the list.
        /// </summary>
        internal async Task<string?> GetPreviewTextAsync(HistoryEntryDto entry)
        {
            if (entry == null) return null;
            if (_previewCache.TryGetValue(entry.Id, out var cached)) return cached;
            if (!_rpc.IsConnected) return entry.SqlText;

            try
            {
                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction,
                    new HistoryActionRequest { Action = HistoryActions.GetFullSql, EntryIds = new[] { entry.Id } },
                    timeoutMs: 10000);
                if (response.Success && response.FullSqlText != null)
                {
                    _previewCache[entry.Id] = response.FullSqlText;
                    return response.FullSqlText;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "HistoryViewModel: preview text for entry {Id} failed", entry.Id);
            }
            return entry.SqlText;
        }

        /// <summary>
        /// Helper: sends a GetFullSql action and returns the full SQL text.
        /// </summary>
        private async Task<string?> GetFullSqlAsync(long entryId)
        {
            if (!_rpc.IsConnected) return null;

            IsLoading = true;
            try
            {
                var actionRequest = new HistoryActionRequest
                {
                    Action = HistoryActions.GetFullSql,
                    EntryIds = new[] { entryId }
                };

                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction, actionRequest, timeoutMs: 10000);

                if (response.Success)
                {
                    return response.FullSqlText;
                }

                Log.Warning("HistoryViewModel: GetFullSql returned error: {Error}", response.Error);
                return null;
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Action Command Implementations (US9)

        private async void ExecuteToggleFavoriteAsync(HistoryEntryDto? entry)
        {
            try
            {
                if (entry != null) await ToggleFavoriteEntryAsync(entry);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: toggle favorite failed");
            }
        }

        /// <summary>
        /// Spec 040 (HIS-04, FR-014): stars or un-stars the row's query. With grouping on, the whole
        /// grouped query (every run) changes, so the star stays after the next run; the row takes
        /// the state the engine returns.
        /// </summary>
        internal async Task ToggleFavoriteEntryAsync(HistoryEntryDto entry)
        {
            if (entry == null || !_rpc.IsConnected) return;

            IsLoading = true;
            try
            {
                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction,
                    new HistoryActionRequest
                    {
                        Action = HistoryActions.ToggleFavorite,
                        EntryIds = new[] { entry.Id },
                        GroupScope = GroupingOn() ? true : (bool?)null,
                    },
                    timeoutMs: 10000);

                if (response.Success)
                {
                    entry.IsFavorite = response.IsFavorite ?? !entry.IsFavorite;
                    await RefreshKeepingSelectionAsync();
                }
                else
                {
                    Log.Warning("HistoryViewModel: ToggleFavorite returned error: {Error}", response.Error);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// What Delete removes: every selected row when several are selected and the command comes
        /// from the keyboard or from one of them; otherwise the row it was invoked on (a row's own
        /// menu acts on its own row, HIS-04).
        /// </summary>
        internal IReadOnlyList<HistoryEntryDto> DeleteTargets(HistoryEntryDto? row)
        {
            var selected = SelectedEntries.ToList();
            if (row == null)
                return selected.Count > 0 ? selected
                    : SelectedEntry != null ? new[] { SelectedEntry } : Array.Empty<HistoryEntryDto>();
            return selected.Count > 1 && selected.Contains(row) ? selected : new[] { row };
        }

        private async void ExecuteDeleteAsync(IReadOnlyList<HistoryEntryDto> entries)
        {
            try
            {
                if (entries.Count > 0) await DeleteEntriesAsync(entries);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: delete failed");
            }
        }

        /// <summary>
        /// Spec 040 (HIS-04, FR-014): removes the row's query after confirmation. With grouping on,
        /// that is the whole grouped query — every run, its versions and its session — so nothing
        /// of it reappears on the next refresh.
        /// </summary>
        internal Task DeleteEntryAsync(HistoryEntryDto entry) =>
            entry == null ? Task.CompletedTask : DeleteEntriesAsync(new[] { entry });

        /// <summary>Removes <paramref name="entries"/> (each with its group's runs) after asking once.</summary>
        internal async Task DeleteEntriesAsync(IReadOnlyList<HistoryEntryDto> entries)
        {
            if (entries == null || entries.Count == 0 || !_rpc.IsConnected) return;
            var question = entries.Count == 1
                ? $"Remove '{HistoryRowDisplay.DisplayNameFor(entries[0])}' and its history?"
                : $"Remove {entries.Count} queries and their history?";
            if (!ConfirmPrompt(question)) return;

            IsLoading = true;
            try
            {
                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction,
                    new HistoryActionRequest
                    {
                        Action = HistoryActions.Delete,
                        EntryIds = entries.Select(e => e.Id).ToArray(),
                        GroupScope = GroupingOn() ? true : (bool?)null,
                    },
                    timeoutMs: 10000);

                if (response.Success)
                {
                    Log.Information("HistoryViewModel: deleted {Count} entries", response.DeletedCount);
                    await RefreshKeepingSelectionAsync();
                }
                else
                {
                    Log.Warning("HistoryViewModel: Delete returned error: {Error}", response.Error);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>Whether History shows one row per query (the "Group repeated runs" setting).</summary>
        private bool GroupingOn()
        {
            try { return SettingsProvider().History.Deduplication; }
            catch (Exception) { return true; }
        }

        /// <summary>
        /// Spec 030 T074 (FR-041) — removes every history entry older than the selected one
        /// (favorites kept; the selected entry itself is kept). Prompts for confirmation first.
        /// </summary>
        private async void ExecuteRemoveOlderThanAsync(HistoryEntryDto? entry)
        {
            try
            {
                if (entry != null) await RemoveOlderThanAsync(entry);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: remove older than failed");
            }
        }

        /// <summary>
        /// Spec 040 (HIS-12): "Remove queries older than this…" on a row, with the row's time in
        /// the confirmation.
        /// </summary>
        internal async Task RemoveOlderThanAsync(HistoryEntryDto entry)
        {
            if (!_rpc.IsConnected) return;

            var when = DateTime.TryParse(entry.ExecutedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? HistoryTimeFormat.Absolute(at.ToLocalTime())
                : entry.ExecutedAt;
            if (!ConfirmPrompt($"Remove all queries older than {when}? Starred queries are kept. This can't be undone.")) return;

            IsLoading = true;
            try
            {
                var actionRequest = new HistoryActionRequest
                {
                    Action = HistoryActions.RemoveOlderThan,
                    EntryIds = new[] { entry.Id },
                    KeepFavorites = true
                };

                var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                    MessageTypes.HistoryAction, actionRequest, timeoutMs: 10000);

                if (response.Success)
                {
                    Log.Information("HistoryViewModel: RemoveOlderThan removed {Count} entries", response.DeletedCount);
                    await RefreshKeepingSelectionAsync();
                    Notify($"Removed {response.DeletedCount} older {(response.DeletedCount == 1 ? "query" : "queries")}.");
                }
                else
                {
                    Log.Warning("HistoryViewModel: RemoveOlderThan returned error: {Error}", response.Error);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Shows a SaveFileDialog and exports history entries in the chosen format.
        /// The export uses the current filter criteria to select entries.
        /// </summary>
        private async void ExecuteExportAsync()
        {
            try
            {
                if (!_rpc.IsConnected) return;

                // Show SaveFileDialog (must be on UI thread — we are, since this is a command handler)
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export SQL History",
                    Filter = "CSV files (*.csv)|*.csv|JSON files (*.json)|*.json|SQL files (*.sql)|*.sql",
                    DefaultExt = ".csv",
                    FileName = "sql-history-export"
                };

                if (dialog.ShowDialog() != true)
                    return;

                // Determine export format from filter index
                int exportFormat;
                switch (dialog.FilterIndex)
                {
                    case 2: exportFormat = 1; break; // JSON
                    case 3: exportFormat = 2; break; // SQL
                    default: exportFormat = 0; break; // CSV
                }

                // Build filter matching current search criteria
                var settings = Core.Config.ConfigManager.Load();
                var filterRequest = new HistorySearchRequest
                {
                    SearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                    Server = SelectedServer,
                    Database = SelectedDatabase,
                    Status = SelectedStatus,
                    DateFrom = DateFrom?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    DateTo = DateTo?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    FavoritesOnly = FavoritesOnly,
                    Deduplicate = false, // Export all matching entries, not deduplicated
                    Offset = 0,
                    Limit = int.MaxValue
                };

                IsLoading = true;
                try
                {
                    var actionRequest = new HistoryActionRequest
                    {
                        Action = HistoryActions.Export,
                        ExportFormat = exportFormat,
                        ExportPath = dialog.FileName,
                        Filter = filterRequest
                    };

                    var response = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                        MessageTypes.HistoryAction, actionRequest, timeoutMs: 60000);

                    if (response.Success)
                    {
                        Log.Information("HistoryViewModel: exported history to {Path}", response.ExportPath);
                        MessageBox.Show(
                            $"History exported successfully to:\n{response.ExportPath}",
                            "Export Complete",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        Log.Warning("HistoryViewModel: Export returned error: {Error}", response.Error);
                        MessageBox.Show(
                            $"Export failed: {response.Error}",
                            "Export Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
                finally
                {
                    IsLoading = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: export failed");
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }

    /// <summary>
    /// Lightweight ICommand implementation for MVVM command binding.
    /// </summary>
    internal class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        : ICommand
    {
        private readonly Action<object?> _execute = execute ?? throw new ArgumentNullException(nameof(execute));

        public bool CanExecute(object? parameter)
        {
            return canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
