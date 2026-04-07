using System;
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

                // SSMS 22 uses a custom menu bar (SSMSMnu.dll) that ignores the
                // standard VSCT IDM_VS_MENU_BAR parent. Programmatically inject
                // our top-level "AKML SQL" popup into DTE's MenuBar command bar.
                EnsureTopLevelMenu(this);

                // Add SQL History button to the Standard toolbar (beside New Query)
                AddHistoryButtonToStandardToolbar();

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

        /// <summary>
        /// SSMS 22's custom menu bar (SSMSMnu.dll) ignores the standard VSCT
        /// IDM_VS_MENU_BAR parent, so menus defined in the CTO are orphaned.
        /// This method programmatically adds an "AKML SQL" popup to the DTE
        /// MenuBar and places our registered commands into it.
        /// </summary>
        private static void EnsureTopLevelMenu(AsyncPackage package)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                dynamic bars = dte.CommandBars;
                if (bars == null) return;

                dynamic menuBar = bars["Menu Bar"];
                if (menuBar == null) return;

                // Check if already added (idempotent on re-load).
                // Strip '&' accelerator chars and trim whitespace to match the
                // VSCT-rendered caption robustly.
                foreach (dynamic ctrl in menuBar.Controls)
                {
                    var cap = ((string)ctrl.Caption).Replace("&", "").Trim();
                    if (cap.Equals("AKML SQL", StringComparison.OrdinalIgnoreCase)) return;
                }

                // Insert before &Window (or at end)
                int insertPos = (int)menuBar.Controls.Count;
                foreach (dynamic ctrl in menuBar.Controls)
                {
                    string cap = ((string)ctrl.Caption).Replace("&", "");
                    if (cap.StartsWith("Window"))
                    {
                        insertPos = (int)ctrl.Index;
                        break;
                    }
                }

                // msoControlPopup = 10
                dynamic popup = menuBar.Controls.Add(10, Type.Missing, Type.Missing, insertPos, true);
                popup.Caption = "AKML SQL";

                // Add registered DTE commands to the popup using guid:id pairs.
                // DTE.Commands wraps OleMenuCommands — clicking invokes our handlers.
                var cmdSetGuid = PackageGuids.AkmlSqlCmdSetString;
                var cmds = new (int id, string label)[]
                {
                    (CommandIds.CmdAbout, "About AKML SQL"),
                    (CommandIds.CmdCheckUpdate, "Check for Updates"),
                    (CommandIds.CmdOptions, "Options"),
                    (CommandIds.CmdSendFeedback, "Send Feedback"),
                    (CommandIds.CmdViewLogs, "View Logs"),
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
                        // Command not found in DTE — add a placeholder button
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
                Log.Information("AKML SQL top-level menu injected into SSMS 22 menu bar");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to create AKML SQL top-level menu (non-fatal)");
            }
        }

        /// <summary>
        /// Adds a "SQL History" button to the SSMS Standard toolbar (next to New Query).
        /// Uses DTE.Commands to wire it to our registered CmdHistoryPanel command.
        /// </summary>
        private static void AddHistoryButtonToStandardToolbar()
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null) return;

                dynamic bars = dte.CommandBars;
                if (bars == null) return;

                dynamic standardBar = null;
                try { standardBar = bars["Standard"]; } catch { return; }
                if (standardBar == null) return;

                // Check if already added
                foreach (dynamic ctrl in standardBar.Controls)
                {
                    if ((string)ctrl.Tag == "AkmlSql.HistoryToolbar") return;
                }

                // Find our History command and add it to the toolbar
                try
                {
                    var cmd = dte.Commands.Item("{" + PackageGuids.AkmlSqlCmdSetString + "}", CommandIds.CmdHistoryPanel);
                    if (cmd != null)
                    {
                        // Add after position 3 (typically after New Query, Open, Save)
                        var ctrl = cmd.AddControl(standardBar, 4);
                        ctrl.Tag = "AkmlSql.HistoryToolbar";
                        Log.Information("SQL History button added to Standard toolbar");
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Could not add History command to Standard toolbar");
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AddHistoryButtonToStandardToolbar failed (non-fatal)");
            }
        }

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
