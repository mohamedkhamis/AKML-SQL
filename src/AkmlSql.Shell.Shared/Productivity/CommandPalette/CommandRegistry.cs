#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Productivity;
using Serilog;

namespace AkmlSql.Shell.Shared.Productivity.CommandPalette
{
    /// <summary>
    /// T033: Static registry of all AKML SQL commands and common SSMS/VS commands
    /// for display in the Command Palette. Scans known command IDs from PackageGuids,
    /// maps them to friendly names and categories, and also includes DTE commands.
    /// </summary>
    internal static class CommandRegistry
    {
        private static List<CommandEntry>? _commands;
        private static readonly object Lock = new();

        /// <summary>
        /// Returns all registered commands, including AKML SQL commands and
        /// common SSMS/VS commands discovered via DTE.
        /// </summary>
        public static IReadOnlyList<CommandEntry> GetAllCommands()
        {
            if (_commands != null) return _commands;

            lock (Lock)
            {
                if (_commands != null) return _commands;

                var commands = new List<CommandEntry>();

                // Register all known AKML SQL commands
                RegisterAkmlCommands(commands);

                // Register common SSMS/VS built-in commands
                RegisterBuiltInCommands(commands);

                // Load usage counts from config
                ApplyUsageCounts(commands);

                _commands = commands;
            }

            return _commands;
        }

        /// <summary>
        /// Increments the usage count for a command and saves to config.
        /// </summary>
        public static void IncrementUsage(string commandId)
        {
            try
            {
                var settings = ConfigManager.Load();
                if (!settings.CommandPalette.UsageCounts.ContainsKey(commandId))
                    settings.CommandPalette.UsageCounts[commandId] = 0;
                settings.CommandPalette.UsageCounts[commandId]++;
                ConfigManager.Save(settings);

                // Update in-memory entry
                var entry = _commands?.FirstOrDefault(c => c.Id == commandId);
                if (entry != null)
                {
                    entry.UsageCount = settings.CommandPalette.UsageCounts[commandId];
                    entry.LastUsed = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CommandRegistry: failed to save usage count for {Id}", commandId);
            }
        }

        /// <summary>
        /// Forces a reload of commands (e.g., after config changes).
        /// </summary>
        public static void Invalidate()
        {
            lock (Lock)
            {
                _commands = null;
            }
        }

        #region AKML SQL commands

        private static void RegisterAkmlCommands(List<CommandEntry> commands)
        {
            // Settings & Info
            commands.Add(Cmd("akml.about", "About AKML SQL", "Settings", CommandIds.CmdAbout));
            commands.Add(Cmd("akml.checkUpdate", "Check for Updates", "Settings", CommandIds.CmdCheckUpdate));
            commands.Add(Cmd("akml.options", "AKML SQL Options", "Settings", CommandIds.CmdOptions));
            commands.Add(Cmd("akml.sendFeedback", "Send Feedback", "Settings", CommandIds.CmdSendFeedback));
            commands.Add(Cmd("akml.viewLogs", "View Logs", "Settings", CommandIds.CmdViewLogs));
            commands.Add(Cmd("akml.refreshCache", "Refresh Schema Cache", "Settings", CommandIds.CmdRefreshCache));

            // Format
            commands.Add(Cmd("akml.formatDocument", "Format SQL Document", "Format", CommandIds.CmdFormatDocument, "Ctrl+K, Y"));
            commands.Add(Cmd("akml.formatSelection", "Format SQL Selection", "Format", CommandIds.CmdFormatSelection, "Ctrl+K, Ctrl+Y"));
            commands.Add(Cmd("akml.casingOnly", "Apply Casing Only", "Format", CommandIds.CmdCasingOnly));
            commands.Add(Cmd("akml.insertSemicolons", "Insert Semicolons", "Format", CommandIds.CmdInsertSemicolons));
            commands.Add(Cmd("akml.removeSemicolons", "Remove Semicolons", "Format", CommandIds.CmdRemoveSemicolons));
            commands.Add(Cmd("akml.expandWildcards", "Expand SELECT *", "Format", CommandIds.CmdExpandWildcards));
            commands.Add(Cmd("akml.qualifyNames", "Qualify Object Names", "Format", CommandIds.CmdQualifyNames));
            commands.Add(Cmd("akml.toggleBrackets", "Toggle Square Brackets", "Format", CommandIds.CmdToggleBrackets));
            commands.Add(Cmd("akml.toggleAs", "Toggle AS Keyword", "Format", CommandIds.CmdToggleAs));
            commands.Add(Cmd("akml.editProfile", "Edit Format Profile", "Format", CommandIds.CmdEditProfile));

            // Refactoring — Lightweight
            commands.Add(Cmd("akml.expandInsertColumns", "Expand INSERT Column List", "Refactoring", CommandIds.CmdExpandInsertColumns));
            commands.Add(Cmd("akml.expandExecParameters", "Expand EXEC Parameters", "Refactoring", CommandIds.CmdExpandExecParameters));
            commands.Add(Cmd("akml.expandUpdateColumns", "Expand UPDATE Column List", "Refactoring", CommandIds.CmdExpandUpdateColumns));
            commands.Add(Cmd("akml.convertOldStyleJoins", "Convert Old-Style Joins", "Refactoring", CommandIds.CmdConvertOldStyleJoins));
            commands.Add(Cmd("akml.addGroupByColumns", "Add GROUP BY Columns", "Refactoring", CommandIds.CmdAddGroupByColumns));
            commands.Add(Cmd("akml.encapsulateBeginEnd", "Encapsulate in BEGIN...END", "Refactoring", CommandIds.CmdEncapsulateBeginEnd));
            commands.Add(Cmd("akml.replaceDeprecatedSyntax", "Replace Deprecated Syntax", "Refactoring", CommandIds.CmdReplaceDeprecatedSyntax));

            // Refactoring — Heavyweight
            commands.Add(Cmd("akml.safeRename", "Safe Rename", "Refactoring", CommandIds.CmdSafeRename));
            commands.Add(Cmd("akml.extractToCte", "Extract to CTE", "Refactoring", CommandIds.CmdExtractToCte));
            commands.Add(Cmd("akml.extractToProc", "Extract to Stored Procedure", "Refactoring", CommandIds.CmdExtractToProc));
            commands.Add(Cmd("akml.extractToDerivedTable", "Extract to Derived Table", "Refactoring", CommandIds.CmdExtractToDerivedTable));
            commands.Add(Cmd("akml.encapsulateAsView", "Encapsulate as View", "Refactoring", CommandIds.CmdEncapsulateAsView));
            commands.Add(Cmd("akml.convertTempToTableVar", "Convert #Temp to @TableVar", "Refactoring", CommandIds.CmdConvertTempToTableVar));
            commands.Add(Cmd("akml.convertTableVarToTemp", "Convert @TableVar to #Temp", "Refactoring", CommandIds.CmdConvertTableVarToTemp));
            commands.Add(Cmd("akml.parameterizeValues", "Parameterize Literal Values", "Refactoring", CommandIds.CmdParameterizeValues));
            // Spec 030 T067 — editor-context refactor commands
            commands.Add(Cmd("akml.inlineExec", "Inline EXEC", "Refactoring", CommandIds.CmdInlineExec));
            commands.Add(Cmd("akml.insertToUpdate", "Convert INSERT to UPDATE", "Refactoring", CommandIds.CmdInsertToUpdate));
            commands.Add(Cmd("akml.inlineStoredProcedure", "Inline Stored Procedure", "Refactoring", CommandIds.CmdInlineStoredProcedure));
            commands.Add(Cmd("akml.scriptAsAlter", "Script as ALTER", "Refactoring", CommandIds.CmdScriptAsAlter));
            commands.Add(Cmd("akml.findInvalidObjects", "Find Invalid Objects", "Refactoring", CommandIds.CmdShowFindInvalidObjects));

            // Analysis
            commands.Add(Cmd("akml.bulkAnalysis", "Run Bulk Code Analysis", "Analysis", CommandIds.CmdBulkAnalysis));
            commands.Add(Cmd("akml.manageRules", "Manage Code Analysis Rules", "Analysis", CommandIds.CmdManageCodeAnalysisRules));
            commands.Add(Cmd("akml.toggleCodeAnalysis", "Toggle Code Analysis", "Analysis", CommandIds.CmdToggleCodeAnalysis));

            // History
            commands.Add(Cmd("akml.historyPanel", "Show SQL History", "History", CommandIds.CmdHistoryPanel, "Ctrl+Alt+H"));
            commands.Add(Cmd("akml.restoreClosedTab", "Restore Closed Tab", "History", CommandIds.CmdRestoreClosedTab, "Ctrl+Shift+T"));

            // Tab Management
            commands.Add(Cmd("akml.closeUnmodified", "Close Unmodified Tabs", "Tab Management", CommandIds.CmdCloseUnmodified));
            commands.Add(Cmd("akml.duplicateTab", "Duplicate Tab", "Tab Management", CommandIds.CmdDuplicateTab));
            commands.Add(Cmd("akml.pinTab", "Pin/Unpin Tab", "Tab Management", CommandIds.CmdPinTab));

            // Navigation
            commands.Add(Cmd("akml.goToDefinition", "Go to Definition", "Navigation", CommandIds.CmdGoToDefinition, "F12"));
            commands.Add(Cmd("akml.peekDefinition", "Peek Definition", "Navigation", CommandIds.CmdPeekDefinition, "Alt+F12"));
            commands.Add(Cmd("akml.findReferences", "Find All References", "Navigation", CommandIds.CmdFindReferences, "Shift+F12"));
            commands.Add(Cmd("akml.objectSearch", "Search Objects", "Navigation", CommandIds.CmdObjectSearch, "Ctrl+T"));
            commands.Add(Cmd("akml.navigateNextStatement", "Navigate to Next Statement", "Navigation", CommandIds.CmdNavigateNextStatement));
            commands.Add(Cmd("akml.navigatePrevStatement", "Navigate to Previous Statement", "Navigation", CommandIds.CmdNavigatePrevStatement));
            commands.Add(Cmd("akml.navigateMatchingPair", "Navigate to Matching Pair", "Navigation", CommandIds.CmdNavigateMatchingPair));

            // Grid
            commands.Add(Cmd("akml.gridFind", "Find in Grid", "Grid", CommandIds.CmdGridFind));
            commands.Add(Cmd("akml.gridExport", "Export Grid to File", "Grid", CommandIds.CmdGridExport));
            commands.Add(Cmd("akml.crudGeneration", "Generate CRUD Scripts", "Grid", CommandIds.CmdCrudGeneration));
            commands.Add(Cmd("akml.documentOutline", "Document Outline", "Editor", CommandIds.CmdDocumentOutline));

            // Command Palette itself
            commands.Add(Cmd("akml.commandPalette", "Command Palette", "General", CommandIds.CmdCommandPalette, "Ctrl+Shift+P"));
        }

        private static CommandEntry Cmd(string id, string name, string category, int cmdId, string? shortcut = null)
        {
            return new CommandEntry
            {
                Id = id,
                Name = name,
                Category = category,
                KeyboardShortcut = shortcut
            };
        }

        #endregion

        #region SSMS / VS built-in commands

        private static void RegisterBuiltInCommands(List<CommandEntry> commands)
        {
            // Common SSMS/VS commands accessible via DTE
            commands.Add(new CommandEntry { Id = "dte.File.NewFile", Name = "New File", Category = "SSMS", KeyboardShortcut = "Ctrl+N" });
            commands.Add(new CommandEntry { Id = "dte.File.OpenFile", Name = "Open File", Category = "SSMS", KeyboardShortcut = "Ctrl+O" });
            commands.Add(new CommandEntry { Id = "dte.File.SaveAll", Name = "Save All", Category = "SSMS", KeyboardShortcut = "Ctrl+Shift+S" });
            commands.Add(new CommandEntry { Id = "dte.Edit.Find", Name = "Find", Category = "SSMS", KeyboardShortcut = "Ctrl+F" });
            commands.Add(new CommandEntry { Id = "dte.Edit.Replace", Name = "Find and Replace", Category = "SSMS", KeyboardShortcut = "Ctrl+H" });
            commands.Add(new CommandEntry { Id = "dte.Edit.GoToLine", Name = "Go to Line", Category = "SSMS", KeyboardShortcut = "Ctrl+G" });
            commands.Add(new CommandEntry { Id = "dte.Edit.ToggleBookmark", Name = "Toggle Bookmark", Category = "SSMS", KeyboardShortcut = "Ctrl+K, Ctrl+K" });
            commands.Add(new CommandEntry { Id = "dte.Edit.NextBookmark", Name = "Next Bookmark", Category = "SSMS", KeyboardShortcut = "Ctrl+K, Ctrl+N" });
            commands.Add(new CommandEntry { Id = "dte.Edit.CommentSelection", Name = "Comment Selection", Category = "SSMS", KeyboardShortcut = "Ctrl+K, Ctrl+C" });
            commands.Add(new CommandEntry { Id = "dte.Edit.UncommentSelection", Name = "Uncomment Selection", Category = "SSMS", KeyboardShortcut = "Ctrl+K, Ctrl+U" });
            commands.Add(new CommandEntry { Id = "dte.Query.Execute", Name = "Execute Query", Category = "SSMS", KeyboardShortcut = "F5" });
            commands.Add(new CommandEntry { Id = "dte.Query.Parse", Name = "Parse Query", Category = "SSMS", KeyboardShortcut = "Ctrl+F5" });
            commands.Add(new CommandEntry { Id = "dte.Query.DisplayEstimatedExecutionPlan", Name = "Display Estimated Execution Plan", Category = "SSMS", KeyboardShortcut = "Ctrl+L" });
            commands.Add(new CommandEntry { Id = "dte.Query.IncludeActualExecutionPlan", Name = "Include Actual Execution Plan", Category = "SSMS", KeyboardShortcut = "Ctrl+M" });
            commands.Add(new CommandEntry { Id = "dte.Query.ResultsToGrid", Name = "Results to Grid", Category = "SSMS" });
            commands.Add(new CommandEntry { Id = "dte.Query.ResultsToText", Name = "Results to Text", Category = "SSMS" });
            commands.Add(new CommandEntry { Id = "dte.Window.CloseAllDocuments", Name = "Close All Documents", Category = "SSMS" });
        }

        #endregion

        #region Usage counts

        private static void ApplyUsageCounts(List<CommandEntry> commands)
        {
            try
            {
                var settings = ConfigManager.Load();
                foreach (var entry in commands)
                {
                    if (settings.CommandPalette.UsageCounts.TryGetValue(entry.Id, out var count))
                    {
                        entry.UsageCount = count;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CommandRegistry: failed to load usage counts");
            }
        }

        #endregion
    }
}
