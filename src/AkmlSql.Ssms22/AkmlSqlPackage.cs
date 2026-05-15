using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Constants = AkmlSql.Core.Constants;
using AkmlSql.Core.Logging;
using AkmlSql.Shell.Shared;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.StatusBar;
using AkmlSql.Shell.Shared.History;
using AkmlSql.Shell.Shared.Productivity.DocumentOutline;
using AkmlSql.Shell.Shared.Productivity.Grid;
using AkmlSql.Shell.Shared.Productivity.Navigation;
using AkmlSql.Shell.Shared.Safety;
using AkmlSql.Shell.Shared.Tabs;
using AkmlSql.Shell.Shared.Update;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Formatting;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Validation;
using AkmlSql.Shell.Shared.Snippets;
using AkmlSql.Shell.Shared.Refactoring;
using Serilog;

namespace AkmlSql.Ssms22
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Constants.ProductName, "AI-powered SQL development assistance", Constants.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad("B7B07F42-6013-4C67-A504-C771CBC7625A", PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideToolWindow(typeof(HistoryToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(DocumentOutlineToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(ReferencesToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(AiChatToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [Guid(PackageGuids.AkmlSqlPackageString)]
    public sealed class AkmlSqlPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            // Register assembly resolver BEFORE anything that loads our dependencies
            ExtensionAssemblyResolver.Register();

            await base.InitializeAsync(cancellationToken, progress);

            // Switch to UI thread for menu registration — do this FIRST
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Register menu commands BEFORE anything else (critical path)
            var commandService = await GetServiceAsync(typeof(IMenuCommandService))
                as OleMenuCommandService;

            if (commandService != null)
            {
                AboutCommand.Initialize(this, commandService);
                CheckUpdateCommand.Initialize(this, commandService);
                OptionsCommand.Initialize(this, commandService);
                SendFeedbackCommand.Initialize(this, commandService);
                ViewLogsCommand.Initialize(this, commandService);
                RefreshCacheCommand.Initialize(this, commandService);

                // Phase 7 — Tab management and safety commands
                RestoreClosedTabCommand.Initialize(this, commandService);
                CloseUnmodifiedCommand.Initialize(this, commandService);
                DuplicateTabCommand.Initialize(this, commandService);
                PinTabCommand.Initialize(this, commandService);

                // Phase 7 US2 — SQL History panel
                HistoryPanelCommand.Initialize(this, commandService);

                // Phase 8 US2 — Grid Copy/Export commands
                GridContextMenuWiring.RegisterCommands(commandService);

                // Phase 8 US7 — Go to Definition & Peek Definition
                GoToDefinitionCommand.Initialize(this, commandService);
                PeekDefinitionCommand.Initialize(this, commandService);

                // Phase 8 US12 — Object Search & Find References
                ObjectSearchCommand.Initialize(this, commandService);
                FindReferencesCommand.Initialize(this, commandService);

                // Phase 8 US3 — Command Palette
                CommandPaletteCommand.Initialize(this, commandService);

                // Phase 8 US4 — Execute Current Statement
                ExecuteCurrentStatementCommand.Initialize(this, commandService);
                ExecuteToCursorCommand.Initialize(this, commandService);

                // Phase 8 US5 — Document Outline
                DocumentOutlineCommand.Initialize(this, commandService);

                // Phase 8 US8 — Navigation commands
                NavigateStatementCommand.Initialize(this, commandService);
                NavigateMatchingPairCommand.Initialize(this, commandService);

                // Formatting commands
                FormatDocumentCommand.Initialize(this, commandService);
                FormatSelectionCommand.Initialize(this, commandService);
                UnformatCommand.Initialize(this, commandService);

                // Phase 9 US2 — AI Explain
                AiExplainCommand.Initialize(this, commandService);

                // Phase 9 US3 — AI Fix
                AiFixCommand.Initialize(this, commandService);

                // Phase 9 US6 — AI Chat Panel
                AiChatPanelCommand.Initialize(this, commandService);

                // Phase 5 — Bulk Analysis
                BulkAnalysisCommand.Initialize(this, commandService);

                // Phase 10 — SQL Prompt Core Parity
                SnippetManagerCommand.Initialize(this, commandService);
                AkmlSql.Shell.Shared.Navigation.BookmarkCommands.Initialize(this, commandService);
                SplitTableCommand.Initialize(this, commandService);
            }

            // Non-critical initialization — failures must not break the extension
            try
            {
                LoggerFactory.Initialize();
                Log.Information("AKML SQL package initializing for SSMS 22 (x64)");

                // Theme system init (spec 016 T015 — FR-007/FR-008/FR-009/FR-018/FR-019).
                try
                {
                    var themeSettings = AkmlSql.Core.Config.ConfigManager.Load();
                    AkmlSql.Shell.Shared.Ui.Theme.HostThemeWatcher.Instance.Initialize();
                    AkmlSql.Shell.Shared.Ui.Theme.ThemeRegistry.Instance.Initialize(
                        themeSettings.Theme,
                        AkmlSql.Shell.Shared.Ui.Theme.HostThemeWatcher.Instance.LastDetectedHostVariant,
                        AkmlSql.Shell.Shared.Ui.Theme.HostThemeWatcher.Instance.IsHighContrast);

                    // Spec 020 (FR-030): one-time first-launch theme migration. Idempotent; safe every launch.
                    AkmlSql.Shell.Shared.Ui.Theme.ThemeMigrationManager.Instance.RunIfNeeded();
                }
                catch (Exception themeEx)
                {
                    Log.Warning(themeEx, "Failed to initialize theme system; falling back to Light defaults");
                }

                var extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                LoadValidator.Validate(extensionDir);

                var statusBar = (IVsStatusbar)await GetServiceAsync(typeof(SVsStatusbar));
                if (statusBar != null)
                {
                    StatusBarManager.SetLoaded(statusBar);
                }

                UpdateLauncher.LaunchIfDue();

                // Launch Engine process for IntelliSense, formatting, analysis
                System.Threading.Tasks.Task.Run(() => EngineLifecycle.LaunchAsync());

                ExecutionCapture.Initialize(this);
                ExecutionInterceptor.Initialize(this);
                TabManagementInitializer.Initialize(this);
                TransactionMonitor.Initialize(this);
                AiSettingsValidator.Initialize();

                // STANDARD TOOLBAR SQL HISTORY BUTTON — guarded add with accumulation
                // recovery. Adds exactly one button if none exists, does nothing if
                // one already exists, and hard-resets the Standard toolbar if more
                // than one exists (recovers from any historical accumulation bug).
                EnsureStandardToolbarHistoryButton();

                // Top-level AKML SQL menu bar popup. This is added ONCE per process
                // via a static guard and was never actually accumulating — only the
                // Standard toolbar button was. We restore this call so the user has
                // the AKML SQL menu back.
                EnsureTopLevelMenu(this);

                Log.Information("AKML SQL package initialized successfully for SSMS 22");
            }
            catch (Exception ex)
            {
                try { Log.Error(ex, "AKML SQL non-critical init failed for SSMS 22"); } catch { /* Intentional: logger may not be initialized */ }

                try
                {
                    var statusBar = (IVsStatusbar)await GetServiceAsync(typeof(SVsStatusbar));
                    if (statusBar != null)
                    {
                        StatusBarManager.SetFailed(statusBar);
                    }
                }
                catch
                {
                    // Swallow — we must never crash the IDE
                }
            }
        }

        // Process-static guard: EnsureStandardToolbarHistoryButton runs at most ONCE per process.
        private static int _toolbarHistoryDone;

        /// <summary>
        /// Ensures the SSMS Standard toolbar has exactly ONE SQL History button
        /// (SQL Prompt-style, placed next to New Query). Handles three cases:
        /// <list type="number">
        /// <item><description><b>Count == 0:</b> add exactly one button via <c>cmd.AddControl</c>.</description></item>
        /// <item><description><b>Count == 1:</b> do nothing — the correct state is already there
        ///   (either from a previous install's persisted binding or from this session's earlier add).</description></item>
        /// <item><description><b>Count &gt; 1:</b> accumulation detected. Call <c>standardBar.Reset()</c>
        ///   to wipe ALL customizations on the bar (the only reliable way to clear the persisted
        ///   state that SSMS 22 hides beneath per-control <c>Delete()</c>), then add exactly one.</description></item>
        /// </list>
        ///
        /// This combines the previous <c>CleanupStaleToolbarEntries</c> and
        /// <c>AddHistoryButtonToStandardToolbar</c> into a single guarded function
        /// so there's exactly one decision point for the toolbar state per process.
        /// </summary>
        private static void EnsureStandardToolbarHistoryButton()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _toolbarHistoryDone, 1, 0) != 0)
            {
                Log.Debug("EnsureStandardToolbarHistoryButton: already run this process, skipping");
                return;
            }

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                dynamic bars = dte.CommandBars;
                if (bars == null) return;

                dynamic standardBar = null;
                try { standardBar = bars["Standard"]; }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Standard toolbar not found — skipping History button setup");
                    return;
                }
                if (standardBar == null) return;

                int sqlHistoryCount = CountSqlHistoryControls(standardBar);
                Log.Information("EnsureStandardToolbarHistoryButton: Standard toolbar has {Count} SQL History control(s)", sqlHistoryCount);

                if (sqlHistoryCount == 1)
                {
                    // Already in the desired state — do nothing.
                    return;
                }

                if (sqlHistoryCount > 1)
                {
                    // Accumulation — reset the entire bar. This wipes all customizations
                    // but is the only reliable way to clear the persisted stale bindings
                    // that SSMS 22 hides beneath per-control Delete() APIs.
                    Log.Information("EnsureStandardToolbarHistoryButton: {Count} duplicates — resetting Standard toolbar", sqlHistoryCount);
                    try { standardBar.Reset(); }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Standard toolbar Reset() failed — falling back to Delete/Hide");
                        var knownLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SQL History" };
                        CleanupControls(standardBar, knownLabels, "Standard", depth: 0);
                    }
                    sqlHistoryCount = CountSqlHistoryControls(standardBar);
                }

                // Count is now 0 (either from the start or after reset). Add exactly one.
                if (sqlHistoryCount == 0)
                {
                    try
                    {
                        var cmd = dte.Commands.Item(
                            "{" + PackageGuids.AkmlSqlCmdSetString + "}",
                            CommandIds.CmdHistoryPanel);
                        if (cmd != null)
                        {
                            // Position 4: typically right after New Query, Open, Save.
                            var ctrl = cmd.AddControl(standardBar, 4);
                            try { ctrl.Tag = "AkmlSql.HistoryToolbar"; } catch { }
                            Log.Information("EnsureStandardToolbarHistoryButton: added SQL History button to Standard toolbar");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to add SQL History button to Standard toolbar");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "EnsureStandardToolbarHistoryButton failed (non-fatal)");
                System.Threading.Interlocked.Exchange(ref _toolbarHistoryDone, 0);
            }
        }

        /// <summary>
        /// Counts the controls on a CommandBar whose caption matches "SQL History"
        /// (case-insensitive, ampersand-stripped). Used by
        /// <see cref="EnsureStandardToolbarHistoryButton"/> to drive its state machine.
        /// </summary>
        private static int CountSqlHistoryControls(dynamic bar)
        {
            int count = 0;
            try
            {
                foreach (dynamic ctrl in bar.Controls)
                {
                    string cap;
                    try { cap = ((string)ctrl.Caption ?? string.Empty).Replace("&", "").Trim(); }
                    catch { continue; }
                    if (cap.Equals("SQL History", StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
            }
            catch { /* bar enumeration failed — treat as 0 */ }
            return count;
        }

        // Process-static guard: EnsureTopLevelMenu runs at most ONCE per process.
        private static int _menuInjected;

        /// <summary>
        /// Programmatically adds the top-level "AKML SQL" popup to the DTE Menu Bar.
        /// This function never accumulated duplicates (only the Standard toolbar did),
        /// so we keep it. The once-per-process guard plus the duplicate-detection
        /// loop at the top prevent any re-addition.
        /// </summary>
        private static void EnsureTopLevelMenu(AsyncPackage package)
        {
            if (System.Threading.Interlocked.CompareExchange(ref _menuInjected, 1, 0) != 0)
            {
                Log.Debug("EnsureTopLevelMenu: already injected this process, skipping");
                return;
            }

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                dynamic bars = dte.CommandBars;
                if (bars == null) return;

                dynamic menuBar = bars["Menu Bar"];
                if (menuBar == null) return;

                // If an AKML SQL popup already exists, REUSE it as-is (don't add
                // commands again — they're persisted from the previous load). This
                // is the key to not accumulating child commands on re-loads.
                foreach (dynamic ctrl in menuBar.Controls)
                {
                    string cap;
                    try { cap = ((string)ctrl.Caption).Replace("&", "").Trim(); }
                    catch { continue; }
                    if (cap.Equals("AKML SQL", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("EnsureTopLevelMenu: AKML SQL popup already present, leaving as-is");
                        return;
                    }
                }

                // Not found — insert a new popup just before the Window menu.
                int insertPos = (int)menuBar.Controls.Count;
                foreach (dynamic ctrl in menuBar.Controls)
                {
                    string cap;
                    try { cap = ((string)ctrl.Caption).Replace("&", ""); }
                    catch { continue; }
                    if (cap.StartsWith("Window"))
                    {
                        insertPos = (int)ctrl.Index;
                        break;
                    }
                }

                // msoControlPopup = 10, Temporary = true
                dynamic popup = menuBar.Controls.Add(10, Type.Missing, Type.Missing, insertPos, true);
                popup.Caption = "AKML SQL";

                // Populate commands via DTE.Commands
                var cmdSetGuid = PackageGuids.AkmlSqlCmdSetString;
                var cmds = new (int id, string label)[]
                {
                    (CommandIds.CmdAbout, "About AKML SQL"),
                    (CommandIds.CmdCheckUpdate, "Check for Updates"),
                    (CommandIds.CmdOptions, "Options"),
                    (CommandIds.CmdSendFeedback, "Send Feedback"),
                    (CommandIds.CmdViewLogs, "View Logs"),
                    (CommandIds.CmdRefreshCache, "Refresh Schema Cache"),
                    (CommandIds.CmdFormatDocument, "Format Document"),
                    (CommandIds.CmdFormatSelection, "Format Selection"),
                    (CommandIds.CmdUnformat, "Unformat Document"),
                    (CommandIds.CmdHistoryPanel, "SQL History"),
                    (CommandIds.CmdRestoreClosedTab, "Restore Closed Tab"),
                    (CommandIds.CmdCommandPalette, "Command Palette"),
                    (CommandIds.CmdDocumentOutline, "Document Outline"),
                    (CommandIds.CmdObjectSearch, "Object Search"),
                };

                dynamic popupBar = popup.CommandBar;
                foreach (var (id, label) in cmds)
                {
                    try
                    {
                        var cmd = dte.Commands.Item("{" + cmdSetGuid + "}", id);
                        if (cmd != null)
                        {
                            cmd.AddControl(popupBar, popupBar.Controls.Count + 1);
                        }
                    }
                    catch
                    {
                        try
                        {
                            dynamic btn = popupBar.Controls.Add(1, Type.Missing, Type.Missing, Type.Missing, true);
                            btn.Caption = label;
                            btn.Enabled = false;
                        }
                        catch { /* Swallow */ }
                    }
                }

                popup.Visible = true;
                Log.Information("AKML SQL top-level menu created with {Count} items", cmds.Length);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to create AKML SQL top-level menu (non-fatal)");
                System.Threading.Interlocked.Exchange(ref _menuInjected, 0);
            }
        }

        /// <summary>
        /// Recursively deletes AKML SQL controls from a CommandBar's Controls collection.
        /// For popup controls, recurses into the child CommandBar. Returns the total
        /// number of controls deleted from this bar and all descendants.
        /// </summary>
        private static int CleanupControls(dynamic bar, HashSet<string> knownLabels, string barName, int depth)
        {
            if (depth > 4) return 0; // safety against infinite recursion

            int deleted = 0;
            var toDelete = new List<object>();
            var toRecurseInto = new List<object>();

            try
            {
                foreach (dynamic ctrl in bar.Controls)
                {
                    string cap;
                    try { cap = ((string)ctrl.Caption ?? string.Empty).Replace("&", "").Trim(); }
                    catch { continue; }

                    if (knownLabels.Contains(cap))
                    {
                        toDelete.Add(ctrl);
                    }
                    else
                    {
                        // Popup control (msoControlPopup = 10) with a foreign caption —
                        // recurse into its child bar so we can find nested AKML SQL entries
                        // (e.g. if they ended up under Tools > External Tools > AKML SQL).
                        try
                        {
                            int ctrlType = (int)ctrl.Type;
                            if (ctrlType == 10) // msoControlPopup
                            {
                                toRecurseInto.Add(ctrl);
                            }
                        }
                        catch { /* control doesn't expose Type */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "CleanupControls: enumeration failed on bar {Bar} (depth {Depth})", barName, depth);
                return 0;
            }

            foreach (dynamic ctrl in toDelete)
            {
                string cap = "";
                try { cap = (string)ctrl.Caption; } catch { }

                // Primary: try to delete the control outright.
                bool deletedOk = false;
                try
                {
                    ctrl.Delete();
                    deletedOk = true;
                    deleted++;
                    Log.Debug("Deleted control '{Caption}' from CommandBar '{Bar}'", cap, barName);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Delete failed for control '{Caption}' on bar {Bar}", cap, barName);
                }

                // Fallback: hide the control so the user doesn't see it even if the
                // persisted entry survived. This handles SSMS 22's quirk where
                // cmd.AddControl bindings persist at a layer beneath Delete().
                if (!deletedOk)
                {
                    try
                    {
                        ctrl.Visible = false;
                        Log.Debug("Hid control '{Caption}' on bar {Bar} (delete failed, visibility=false fallback)", cap, barName);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to hide control '{Caption}' on bar {Bar}", cap, barName);
                    }
                }
            }

            // Recurse into non-AKML popups
            foreach (dynamic popup in toRecurseInto)
            {
                try
                {
                    dynamic childBar = popup.CommandBar;
                    if (childBar != null)
                    {
                        string childName = barName + " > " + (string)popup.Caption;
                        deleted += CleanupControls(childBar, knownLabels, childName, depth + 1);
                    }
                }
                catch { /* not all popups expose a child CommandBar */ }
            }

            return deleted;
        }

        // AddHistoryButtonToStandardToolbar was removed — every call to
        // cmd.AddControl(standardBar, 4) persisted a new binding at a layer beneath
        // our Delete() calls, causing 4 → 6 → 8 → 9 SQL History duplicates to
        // accumulate across package reloads. Users can still open SQL History via:
        //   • The keyboard shortcut Ctrl+Alt+H
        //   • The AKML SQL top-level menu (injected by EnsureTopLevelMenu)
        //   • The editor margin toolbar on SQL files (WPF, no DTE involvement)

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TransactionMonitor.Shutdown();
                LoggerFactory.Shutdown();
            }

            base.Dispose(disposing);
        }
    }
}
