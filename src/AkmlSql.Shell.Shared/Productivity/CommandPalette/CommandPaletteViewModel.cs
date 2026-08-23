#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AkmlSql.Core.Models.Productivity;
using AkmlSql.Shell.Shared.Refactoring;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.CommandPalette
{
    /// <summary>
    /// T035: ViewModel for the Command Palette. Implements INotifyPropertyChanged.
    /// Manages search text, filtered commands, and selection state.
    /// Ranking formula: 0.7 * usageFrequency + 0.3 * matchScore.
    /// </summary>
    internal sealed class CommandPaletteViewModel : INotifyPropertyChanged
    {
        private string _searchText = string.Empty;
        private int _selectedIndex;
        private readonly double _usageWeight = 0.7;
        private readonly double _matchWeight = 0.3;

        // Spec 030 T086 / FR-045 — active-editor context for the DB-object provider. Resolved once at
        // construction (while the SQL editor is still the active text view, before the palette steals
        // focus): the session id targets the live connection/schema cache, the view is the insertion
        // target when a DB object is selected.
        private readonly IWpfTextView? _activeView;
        private readonly string? _sessionId;

        // Monotonic generation stamp so a slow async DB-object search that returns after the user has
        // typed further (or cleared the box) is dropped instead of polluting the current result list.
        private int _searchGeneration;

        // Debounce window before firing the DB-object IPC on each keystroke.
        private const int DbSearchDebounceMs = 150;

        public CommandPaletteViewModel()
        {
            FilteredCommands = new ObservableCollection<CommandEntry>();

            // Capture the active editor before the palette window is shown.
            try
            {
                var ctx = RefactorCommandHelper.TryGetActiveEditor();
                if (ctx != null)
                {
                    _activeView = ctx.View;
                    _sessionId = ctx.SessionId;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CommandPaletteViewModel: failed to resolve active editor for DB-object search");
            }

            RefreshFilteredCommands();
        }

        /// <summary>
        /// The current search text typed by the user.
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                RefreshFilteredCommands();
            }
        }

        /// <summary>
        /// The filtered and ranked list of commands to display.
        /// </summary>
        public ObservableCollection<CommandEntry> FilteredCommands { get; }

        /// <summary>
        /// The index of the currently selected command in the list.
        /// </summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Raised when the user selects a command to execute. The event argument is the command ID.
        /// </summary>
        public event Action<string>? CommandExecuted;

        /// <summary>
        /// Raised when the palette should be closed (e.g., after command execution or Escape).
        /// </summary>
        public event Action? CloseRequested;

        /// <summary>
        /// Moves selection up by one.
        /// </summary>
        public void MoveUp()
        {
            if (SelectedIndex > 0)
                SelectedIndex--;
        }

        /// <summary>
        /// Moves selection down by one.
        /// </summary>
        public void MoveDown()
        {
            if (SelectedIndex < FilteredCommands.Count - 1)
                SelectedIndex++;
        }

        /// <summary>
        /// Executes the currently selected command.
        /// </summary>
        public void ExecuteSelected()
        {
            if (SelectedIndex < 0 || SelectedIndex >= FilteredCommands.Count)
                return;

            var entry = FilteredCommands[SelectedIndex];
            ExecuteCommand(entry);
        }

        /// <summary>
        /// Executes a specific command entry.
        /// </summary>
        public void ExecuteCommand(CommandEntry entry)
        {
            try
            {
                // Spec 030 T086 — DB-object results insert the schema-qualified name at the caret in the
                // active editor rather than executing a command. They are not registry commands, so their
                // usage count is not tracked.
                if (DbObjectProvider.IsDbObject(entry.Id))
                {
                    InsertDbObjectAtCaret(DbObjectProvider.GetInsertText(entry.Id));
                    CommandExecuted?.Invoke(entry.Id);
                    CloseRequested?.Invoke();
                    return;
                }

                // Increment usage count
                CommandRegistry.IncrementUsage(entry.Id);

                // Execute via DTE or known command handler
                if (entry.Id.StartsWith("dte."))
                {
                    ExecuteDteCommand(entry.Id);
                }
                else
                {
                    // For AKML commands, invoke the OleMenuCommand
                    ExecuteAkmlCommand(entry.Id);
                }

                CommandExecuted?.Invoke(entry.Id);
                CloseRequested?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CommandPalette: failed to execute command {Id}", entry.Id);
            }
        }

        /// <summary>
        /// Requests the palette to close.
        /// </summary>
        public void RequestClose()
        {
            CloseRequested?.Invoke();
        }

        #region Filtering and ranking

        private void RefreshFilteredCommands()
        {
            try
            {
                var allCommands = CommandRegistry.GetAllCommands();
                var matched = FuzzyMatcher.Match(_searchText, allCommands);

                // Compute max usage for normalization
                var maxUsage = allCommands.Max(c => c.UsageCount);
                if (maxUsage == 0) maxUsage = 1;

                // Compute max match score for normalization
                var maxMatchScore = matched.Count > 0 ? matched.Max(m => m.Score) : 1.0;
                if (maxMatchScore <= 0) maxMatchScore = 1.0;

                // Rank by combined score: 0.7 * usageFreq + 0.3 * matchScore
                var ranked = matched
                    .Select(m =>
                    {
                        var usageNorm = (double)m.Entry.UsageCount / maxUsage;
                        var matchNorm = m.Score / maxMatchScore;
                        var combined = _usageWeight * usageNorm + _matchWeight * matchNorm;
                        return (Entry: m.Entry, CombinedScore: combined);
                    })
                    .OrderByDescending(r => r.CombinedScore)
                    .Take(50) // Limit to 50 results for performance
                    .Select(r => r.Entry)
                    .ToList();

                FilteredCommands.Clear();
                foreach (var entry in ranked)
                {
                    FilteredCommands.Add(entry);
                }

                // Reset selection to first item
                SelectedIndex = FilteredCommands.Count > 0 ? 0 : -1;

                // Spec 030 T086 — fire a debounced DB-object search that appends matches when they return.
                var generation = ++_searchGeneration;
                _ = SearchDbObjectsAsync(generation, _searchText);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CommandPaletteViewModel: failed to refresh filtered commands");
            }
        }

        /// <summary>
        /// Spec 030 T086 / FR-045. Debounced, off-UI-thread query for database objects matching the
        /// current search text. Appends matches to <see cref="FilteredCommands"/> on the UI thread, but
        /// only if this call is still the newest (generation) and the search text has not changed — so a
        /// stale response never pollutes a later result set. Degrades silently (no DB objects) when the
        /// engine/connection is unavailable.
        /// </summary>
        private async Task SearchDbObjectsAsync(int generation, string query)
        {
            try
            {
                // No editor resolved → no session to target and nowhere to insert; skip entirely.
                if (_activeView == null || string.IsNullOrEmpty(_sessionId))
                    return;

                if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < DbObjectProvider.MinChars)
                    return;

                // Debounce: coalesce bursts of keystrokes. Bail early if superseded during the wait.
                await Task.Delay(DbSearchDebounceMs).ConfigureAwait(false);
                if (generation != _searchGeneration)
                    return;

                var entries = await DbObjectProvider.SearchAsync(_sessionId, query).ConfigureAwait(false);
                if (entries.Count == 0)
                    return;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Re-check freshness now that we are back on the UI thread: another keystroke may have
                // bumped the generation (and already cleared/rebuilt the list) while we were awaiting.
                if (generation != _searchGeneration || !string.Equals(query, _searchText, StringComparison.Ordinal))
                    return;

                foreach (var entry in entries)
                {
                    FilteredCommands.Add(entry);
                }

                if (SelectedIndex < 0 && FilteredCommands.Count > 0)
                    SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CommandPaletteViewModel: DB-object search failed");
            }
        }

        /// <summary>
        /// Inserts the schema-qualified object name at the caret (replacing any active selection) in the
        /// editor captured when the palette opened. Must run on the UI thread.
        /// </summary>
        private void InsertDbObjectAtCaret(string qualifiedName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var view = _activeView;
            if (view == null || string.IsNullOrEmpty(qualifiedName))
                return;

            try
            {
                using (var edit = view.TextBuffer.CreateEdit())
                {
                    if (!view.Selection.IsEmpty)
                    {
                        int start = view.Selection.Start.Position.Position;
                        int length = view.Selection.End.Position.Position - start;
                        edit.Replace(start, length, qualifiedName);
                    }
                    else
                    {
                        edit.Insert(view.Caret.Position.BufferPosition.Position, qualifiedName);
                    }
                    edit.Apply();
                }

                try { view.VisualElement?.Focus(); } catch { /* focus is best-effort */ }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CommandPaletteViewModel: failed to insert DB object '{Name}'", qualifiedName);
            }
        }

        #endregion

        #region Command execution

        private static void ExecuteDteCommand(string commandId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null)
                {
                    Log.Warning("CommandPalette: DTE not available for command {Id}", commandId);
                    return;
                }

                // Map from our ID to the DTE command name
                var dteCommandName = MapToDteCommandName(commandId);
                if (!string.IsNullOrEmpty(dteCommandName))
                {
                    dte.ExecuteCommand(dteCommandName);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CommandPalette: failed to execute DTE command {Id}", commandId);
            }
        }

        private static string MapToDteCommandName(string commandId)
        {
            // Remove "dte." prefix and map to DTE command format
            var suffix = commandId.Substring(4); // Remove "dte."
            return suffix switch
            {
                "File.NewFile" => "File.NewFile",
                "File.OpenFile" => "File.OpenFile",
                "File.SaveAll" => "File.SaveAll",
                "Edit.Find" => "Edit.Find",
                "Edit.Replace" => "Edit.Replace",
                "Edit.GoToLine" => "Edit.GoTo",
                "Edit.ToggleBookmark" => "Edit.ToggleBookmark",
                "Edit.NextBookmark" => "Edit.NextBookmark",
                "Edit.CommentSelection" => "Edit.CommentSelection",
                "Edit.UncommentSelection" => "Edit.UncommentSelection",
                "Query.Execute" => "Query.Execute",
                "Query.Parse" => "Query.Parse",
                "Query.DisplayEstimatedExecutionPlan" => "Query.DisplayEstimatedExecutionPlan",
                "Query.IncludeActualExecutionPlan" => "Query.IncludeActualExecutionPlan",
                "Query.ResultsToGrid" => "Query.ResultsToGrid",
                "Query.ResultsToText" => "Query.ResultsToText",
                "Window.CloseAllDocuments" => "Window.CloseAllDocuments",
                _ => suffix // Pass through as-is
            };
        }

        private static void ExecuteAkmlCommand(string commandId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // Find the matching CommandID from the registry
                var cmdIdValue = GetCommandIdValue(commandId);
                if (cmdIdValue < 0)
                {
                    Log.Debug("CommandPalette: no command ID mapping for {Id}", commandId);
                    return;
                }

                // Execute via the VS menu command service
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte != null)
                {
                    // Use DTE to execute via command GUID:ID
                    var guidStr = PackageGuids.AkmlSqlCmdSetString;
                    dte.Commands.Raise(
                        "{" + guidStr + "}",
                        cmdIdValue,
                        null,
                        null);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CommandPalette: failed to execute AKML command {Id}", commandId);
            }
        }

        /// <summary>
        /// Maps a command string ID to its numeric CommandIds value.
        /// </summary>
        private static int GetCommandIdValue(string commandId)
        {
            return commandId switch
            {
                "akml.about" => CommandIds.CmdAbout,
                "akml.checkUpdate" => CommandIds.CmdCheckUpdate,
                "akml.options" => CommandIds.CmdOptions,
                "akml.sendFeedback" => CommandIds.CmdSendFeedback,
                "akml.viewLogs" => CommandIds.CmdViewLogs,
                "akml.refreshCache" => CommandIds.CmdRefreshCache,
                "akml.formatDocument" => CommandIds.CmdFormatDocument,
                "akml.formatSelection" => CommandIds.CmdFormatSelection,
                "akml.casingOnly" => CommandIds.CmdCasingOnly,
                "akml.insertSemicolons" => CommandIds.CmdInsertSemicolons,
                "akml.removeSemicolons" => CommandIds.CmdRemoveSemicolons,
                "akml.expandWildcards" => CommandIds.CmdExpandWildcards,
                "akml.qualifyNames" => CommandIds.CmdQualifyNames,
                "akml.toggleBrackets" => CommandIds.CmdToggleBrackets,
                "akml.toggleAs" => CommandIds.CmdToggleAs,
                "akml.formatStyles" => CommandIds.CmdFormatStyles,
                "akml.disableFormattingForSelection" => CommandIds.CmdDisableFormattingForSelection,
                "akml.bulkFormat" => CommandIds.CmdBulkFormat,
                "akml.expandInsertColumns" => CommandIds.CmdExpandInsertColumns,
                "akml.expandExecParameters" => CommandIds.CmdExpandExecParameters,
                "akml.expandUpdateColumns" => CommandIds.CmdExpandUpdateColumns,
                "akml.convertOldStyleJoins" => CommandIds.CmdConvertOldStyleJoins,
                "akml.addGroupByColumns" => CommandIds.CmdAddGroupByColumns,
                "akml.encapsulateBeginEnd" => CommandIds.CmdEncapsulateBeginEnd,
                "akml.replaceDeprecatedSyntax" => CommandIds.CmdReplaceDeprecatedSyntax,
                "akml.safeRename" => CommandIds.CmdSafeRename,
                "akml.extractToCte" => CommandIds.CmdExtractToCte,
                "akml.extractToProc" => CommandIds.CmdExtractToProc,
                "akml.extractToDerivedTable" => CommandIds.CmdExtractToDerivedTable,
                "akml.encapsulateAsView" => CommandIds.CmdEncapsulateAsView,
                "akml.convertTempToTableVar" => CommandIds.CmdConvertTempToTableVar,
                "akml.convertTableVarToTemp" => CommandIds.CmdConvertTableVarToTemp,
                "akml.parameterizeValues" => CommandIds.CmdParameterizeValues,
                "akml.inlineExec" => CommandIds.CmdInlineExec,
                "akml.insertToUpdate" => CommandIds.CmdInsertToUpdate,
                "akml.inlineStoredProcedure" => CommandIds.CmdInlineStoredProcedure,
                "akml.scriptAsAlter" => CommandIds.CmdScriptAsAlter,
                "akml.findInvalidObjects" => CommandIds.CmdShowFindInvalidObjects,
                "akml.toggleCodeAnalysis" => CommandIds.CmdToggleCodeAnalysis,
                "akml.bulkAnalysis" => CommandIds.CmdBulkAnalysis,
                "akml.manageRules" => CommandIds.CmdManageCodeAnalysisRules,
                "akml.historyPanel" => CommandIds.CmdHistoryPanel,
                "akml.restoreClosedTab" => CommandIds.CmdRestoreClosedTab,
                "akml.closeUnmodified" => CommandIds.CmdCloseUnmodified,
                "akml.duplicateTab" => CommandIds.CmdDuplicateTab,
                "akml.pinTab" => CommandIds.CmdPinTab,
                "akml.goToDefinition" => CommandIds.CmdGoToDefinition,
                "akml.peekDefinition" => CommandIds.CmdPeekDefinition,
                "akml.findReferences" => CommandIds.CmdFindReferences,
                "akml.objectSearch" => CommandIds.CmdObjectSearch,
                "akml.navigateNextStatement" => CommandIds.CmdNavigateNextStatement,
                "akml.navigatePrevStatement" => CommandIds.CmdNavigatePrevStatement,
                "akml.navigateMatchingPair" => CommandIds.CmdNavigateMatchingPair,
                "akml.gridFind" => CommandIds.CmdGridFind,
                "akml.gridExport" => CommandIds.CmdGridExport,
                "akml.crudGeneration" => CommandIds.CmdCrudGeneration,
                "akml.documentOutline" => CommandIds.CmdDocumentOutline,
                "akml.commandPalette" => CommandIds.CmdCommandPalette,
                "akml.snippetCreateFromSelection" => CommandIds.CmdSnippetCreateFromSelection,
                "akml.snippetSurroundWith" => CommandIds.CmdSnippetSurroundWith,
                _ => -1
            };
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
