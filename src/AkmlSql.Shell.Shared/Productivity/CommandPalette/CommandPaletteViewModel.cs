#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AkmlSql.Core.Models.Productivity;
using Microsoft.VisualStudio.Shell;
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

        public CommandPaletteViewModel()
        {
            FilteredCommands = new ObservableCollection<CommandEntry>();
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
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CommandPaletteViewModel: failed to refresh filtered commands");
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
                "akml.editProfile" => CommandIds.CmdEditProfile,
                "akml.disableFormattingForSelection" => CommandIds.CmdDisableFormattingForSelection,
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
