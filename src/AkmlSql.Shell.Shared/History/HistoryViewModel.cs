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
        private int _totalCount;
        private bool _isLoading;
        private int _currentOffset;
        private HistoryEntryDto? _selectedEntry;
        private const int PageSize = 100;

        /// <summary>
        /// Raised when a "Compare" action completes with two SQL texts for side-by-side diff.
        /// The event handler receives the left and right SQL strings.
        /// </summary>
        internal event Action<string, string>? CompareRequested;

        /// <summary>
        /// Raised when an "Open in New Tab" action completes with the full SQL text.
        /// The event handler receives the SQL text to open.
        /// </summary>
        internal event Action<string>? OpenInNewTabRequested;

        /// <summary>
        /// Raised when a "Re-execute" action completes with the full SQL text.
        /// The event handler receives the SQL text to re-execute.
        /// </summary>
        internal event Action<string>? ReExecuteRequested;

        public HistoryViewModel()
        {
            Entries = new ObservableCollection<HistoryEntryDto>();
            SelectedEntries = new ObservableCollection<HistoryEntryDto>();
            Servers = new ObservableCollection<string>();
            Databases = new ObservableCollection<string>();

            // Search/filter commands
            SearchCommand = new RelayCommand(_ => ExecuteSearchAsync(), _ => !IsLoading);
            ClearFiltersCommand = new RelayCommand(_ => ExecuteClearFiltersAsync(), _ => !IsLoading);
            LoadMoreCommand = new RelayCommand(_ => ExecuteLoadMoreAsync(), _ => !IsLoading && HasMoreEntries);

            // US3 action commands
            CopySqlCommand = new RelayCommand(_ => ExecuteCopySqlAsync(), _ => !IsLoading && SelectedEntry != null);
            OpenInNewTabCommand = new RelayCommand(_ => ExecuteOpenInNewTabAsync(), _ => !IsLoading && SelectedEntry != null);
            ReExecuteCommand = new RelayCommand(_ => ExecuteReExecuteAsync(), _ => !IsLoading && SelectedEntry != null);
            CompareCommand = new RelayCommand(_ => ExecuteCompareAsync(), _ => !IsLoading && SelectedEntries.Count == 2);

            // US9 action commands
            ToggleFavoriteCommand = new RelayCommand(_ => ExecuteToggleFavoriteAsync(), _ => !IsLoading && SelectedEntry != null);
            DeleteCommand = new RelayCommand(_ => ExecuteDeleteAsync(), _ => !IsLoading && SelectedEntries.Count > 0);
            ExportCommand = new RelayCommand(_ => ExecuteExportAsync(), _ => !IsLoading);
        }

        #region Properties

        /// <summary>Full-text search query.</summary>
        public string SearchText
        {
            get => _searchText;
            set => SetField(ref _searchText, value);
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

        /// <summary>Whether there are more entries available beyond the current page.</summary>
        public bool HasMoreEntries => _currentOffset + Entries.Count < TotalCount;

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
                SearchText = string.Empty;
                SelectedServer = null;
                SelectedDatabase = null;
                SelectedStatus = null;
                DateFrom = null;
                DateTo = null;
                FavoritesOnly = false;

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

        private async Task SearchInternalAsync(bool resetOffset)
        {
            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected)
            {
                Log.Debug("HistoryViewModel: engine not connected, cannot search");
                return;
            }

            IsLoading = true;

            try
            {
                if (resetOffset)
                {
                    _currentOffset = 0;
                }
                else
                {
                    _currentOffset += PageSize;
                }

                // Read deduplication preference from config
                var settings = Core.Config.ConfigManager.Load();
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

                // Determine effective open/closed filter from tab or search prefix
                bool? effectiveIsOpen = IsOpenFilter;
                if (parsed.HasPrefixes && parsed.OpenFilter.HasValue)
                    effectiveIsOpen = parsed.OpenFilter.Value;

                var request = new HistorySearchRequest
                {
                    SearchText = effectiveSearchText,
                    Server = effectiveServer,
                    Database = effectiveDatabase,
                    Status = SelectedStatus,
                    DateFrom = DateFrom?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    DateTo = DateTo?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                    FavoritesOnly = effectiveFavoritesOnly,
                    Deduplicate = deduplicate,
                    Offset = _currentOffset,
                    Limit = PageSize,
                    IsOpen = effectiveIsOpen
                };

                var response = await client.SendRequestAsync<HistorySearchResponse, HistorySearchRequest>(
                    MessageTypes.HistorySearch, request, timeoutMs: 10000);

                if (response.Success)
                {
                    if (resetOffset)
                    {
                        Entries.Clear();
                    }

                    foreach (var entry in response.Entries)
                    {
                        Entries.Add(entry);
                    }

                    TotalCount = response.TotalCount;
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
        private async void ExecuteCopySqlAsync()
        {
            try
            {
                if (SelectedEntry == null) return;

                var fullSql = await GetFullSqlAsync(SelectedEntry.Id);
                if (fullSql != null)
                {
                    Clipboard.SetText(fullSql);
                    Log.Debug("HistoryViewModel: copied full SQL for entry {Id} to clipboard", SelectedEntry.Id);
                }
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
        private async void ExecuteOpenInNewTabAsync()
        {
            try
            {
                if (SelectedEntry == null) return;

                var fullSql = await GetFullSqlAsync(SelectedEntry.Id);
                if (fullSql != null)
                {
                    OpenInNewTabRequested?.Invoke(fullSql);
                }
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
        private async void ExecuteReExecuteAsync()
        {
            try
            {
                if (SelectedEntry == null) return;

                var fullSql = await GetFullSqlAsync(SelectedEntry.Id);
                if (fullSql != null)
                {
                    ReExecuteRequested?.Invoke(fullSql);
                }
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
        private async void ExecuteCompareAsync()
        {
            try
            {
                if (SelectedEntries.Count != 2) return;

                var id1 = SelectedEntries[0].Id;
                var id2 = SelectedEntries[1].Id;

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                IsLoading = true;
                try
                {
                    var actionRequest = new HistoryActionRequest
                    {
                        Action = HistoryActions.GetDiff,
                        EntryIds = new[] { id1, id2 }
                    };

                    var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                        MessageTypes.HistoryAction, actionRequest, timeoutMs: 10000);

                    if (response.Success && response.DiffLeftSql != null && response.DiffRightSql != null)
                    {
                        CompareRequested?.Invoke(response.DiffLeftSql, response.DiffRightSql);
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

        /// <summary>
        /// Helper: sends a GetFullSql action and returns the full SQL text.
        /// </summary>
        private async Task<string?> GetFullSqlAsync(long entryId)
        {
            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected) return null;

            IsLoading = true;
            try
            {
                var actionRequest = new HistoryActionRequest
                {
                    Action = HistoryActions.GetFullSql,
                    EntryIds = new[] { entryId }
                };

                var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
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

        /// <summary>
        /// Sends a ToggleFavorite action to the engine, then refreshes the entries list.
        /// </summary>
        private async void ExecuteToggleFavoriteAsync()
        {
            try
            {
                if (SelectedEntry == null) return;

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                IsLoading = true;
                try
                {
                    var actionRequest = new HistoryActionRequest
                    {
                        Action = HistoryActions.ToggleFavorite,
                        EntryIds = new[] { SelectedEntry.Id }
                    };

                    var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                        MessageTypes.HistoryAction, actionRequest, timeoutMs: 10000);

                    if (response.Success)
                    {
                        // Toggle the local entry's favorite state for immediate UI feedback
                        SelectedEntry.IsFavorite = !SelectedEntry.IsFavorite;
                        // Refresh the list to reflect the change
                        await SearchInternalAsync(resetOffset: true);
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
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: toggle favorite failed");
            }
        }

        /// <summary>
        /// Sends a Delete action for all selected entries, then refreshes the entries list.
        /// </summary>
        private async void ExecuteDeleteAsync()
        {
            try
            {
                if (SelectedEntries.Count == 0) return;

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

                var entryIds = SelectedEntries.Select(e => e.Id).ToArray();

                IsLoading = true;
                try
                {
                    var actionRequest = new HistoryActionRequest
                    {
                        Action = HistoryActions.Delete,
                        EntryIds = entryIds
                    };

                    var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                        MessageTypes.HistoryAction, actionRequest, timeoutMs: 10000);

                    if (response.Success)
                    {
                        Log.Information("HistoryViewModel: deleted {Count} entries", entryIds.Length);
                        await SearchInternalAsync(resetOffset: true);
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
            catch (Exception ex)
            {
                Log.Error(ex, "HistoryViewModel: delete failed");
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
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected) return;

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

                    var response = await client.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
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
