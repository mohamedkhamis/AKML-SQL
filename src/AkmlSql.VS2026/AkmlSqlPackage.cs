using System;
using System.ComponentModel.Design;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
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

namespace AkmlSql.VS2026
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Constants.ProductName, "AI-powered SQL development assistance", Constants.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
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
                TryInitCommand("AboutCommand", () => AboutCommand.Initialize(this, commandService));
                TryInitCommand("CheckUpdateCommand", () => CheckUpdateCommand.Initialize(this, commandService));
                TryInitCommand("OptionsCommand", () => OptionsCommand.Initialize(this, commandService));
                TryInitCommand("FormatStylesCommand", () => FormatStylesCommand.Initialize(this, commandService));
                // Spec 030 T053 — Manage Code Analysis Rules dialog
                TryInitCommand("ManageRulesCommand", () => AkmlSql.Shell.Shared.Analysis.ManageRulesCommand.Initialize(this, commandService));
                // Spec 030 T067 — editor-context refactor commands
                TryInitCommand("InlineExecCommand", () => InlineExecCommand.Initialize(this, commandService));
                TryInitCommand("InsertToUpdateCommand", () => InsertToUpdateCommand.Initialize(this, commandService));
                TryInitCommand("InlineStoredProcedureCommand", () => InlineStoredProcedureCommand.Initialize(this, commandService));
                TryInitCommand("ScriptAsAlterCommand", () => ScriptAsAlterCommand.Initialize(this, commandService));
                TryInitCommand("FindInvalidObjectsCommand", () => FindInvalidObjectsCommand.Initialize(this, commandService));
                // Spec 030 T062 — database-wide Smart Rename (FR-018)
                TryInitCommand("SafeRenameCommand", () => SafeRenameCommand.Initialize(this, commandService));
                // Spec 030 T056 — toggle code analysis on/off
                TryInitCommand("ToggleCodeAnalysisCommand", () => AkmlSql.Shell.Shared.Analysis.ToggleCodeAnalysisCommand.Initialize(this, commandService));
                // Spec 030 T068 — disable formatting for selection
                TryInitCommand("DisableFormattingForSelectionCommand", () => DisableFormattingForSelectionCommand.Initialize(this, commandService));
                TryInitCommand("SendFeedbackCommand", () => SendFeedbackCommand.Initialize(this, commandService));
                TryInitCommand("ViewLogsCommand", () => ViewLogsCommand.Initialize(this, commandService));
                TryInitCommand("RefreshCacheCommand", () => RefreshCacheCommand.Initialize(this, commandService));

                // Phase 7 — Tab management and safety commands
                TryInitCommand("RestoreClosedTabCommand", () => RestoreClosedTabCommand.Initialize(this, commandService));
                TryInitCommand("CloseUnmodifiedCommand", () => CloseUnmodifiedCommand.Initialize(this, commandService));
                TryInitCommand("DuplicateTabCommand", () => DuplicateTabCommand.Initialize(this, commandService));
                TryInitCommand("PinTabCommand", () => PinTabCommand.Initialize(this, commandService));

                // Phase 7 US2 — SQL History panel
                TryInitCommand("HistoryPanelCommand", () => HistoryPanelCommand.Initialize(this, commandService));

                // Phase 8 US7 — Go to Definition & Peek Definition
                TryInitCommand("GoToDefinitionCommand", () => GoToDefinitionCommand.Initialize(this, commandService));
                TryInitCommand("PeekDefinitionCommand", () => PeekDefinitionCommand.Initialize(this, commandService));

                // Phase 8 US12 — Object Search & Find References
                TryInitCommand("ObjectSearchCommand", () => ObjectSearchCommand.Initialize(this, commandService));
                TryInitCommand("FindReferencesCommand", () => FindReferencesCommand.Initialize(this, commandService));

                // Phase 8 US2 — Grid Copy/Export commands
                TryInitCommand("GridContextMenuWiring", () => GridContextMenuWiring.RegisterCommands(commandService));

                // Phase 8 US3 — Command Palette
                TryInitCommand("CommandPaletteCommand", () => CommandPaletteCommand.Initialize(this, commandService));

                // Phase 8 US4 — Execute Current Statement
                TryInitCommand("ExecuteCurrentStatementCommand", () => ExecuteCurrentStatementCommand.Initialize(this, commandService));
                TryInitCommand("ExecuteToCursorCommand", () => ExecuteToCursorCommand.Initialize(this, commandService));

                // Phase 8 US5 — Document Outline
                TryInitCommand("DocumentOutlineCommand", () => DocumentOutlineCommand.Initialize(this, commandService));

                // Phase 8 US8 — Navigation commands
                TryInitCommand("NavigateStatementCommand", () => NavigateStatementCommand.Initialize(this, commandService));
                TryInitCommand("NavigateMatchingPairCommand", () => NavigateMatchingPairCommand.Initialize(this, commandService));

                // Phase 9 US2 — AI Explain
                TryInitCommand("AiExplainCommand", () => AiExplainCommand.Initialize(this, commandService));

                // Phase 9 US3 — AI Fix
                TryInitCommand("AiFixCommand", () => AiFixCommand.Initialize(this, commandService));

                // Phase 9 US6 — AI Chat Panel
                TryInitCommand("AiChatPanelCommand", () => AiChatPanelCommand.Initialize(this, commandService));

                // Phase 5 — Bulk Analysis
                TryInitCommand("BulkAnalysisCommand", () => BulkAnalysisCommand.Initialize(this, commandService));

                // Phase 10 — SQL Prompt Core Parity
                TryInitCommand("SnippetManagerCommand", () => SnippetManagerCommand.Initialize(this, commandService));
                // Spec 030 T044/T045 — Create Snippet from Selection + Surround With
                TryInitCommand("CreateFromSelectionCommand", () => AkmlSql.Shell.Shared.Snippets.CreateFromSelectionCommand.Initialize(this, commandService));
                TryInitCommand("SurroundWithCommand", () => AkmlSql.Shell.Shared.Snippets.SurroundWithCommand.Initialize(this, commandService));
                // Spec 030 T087 — Bulk Format wizard (FR-046)
                TryInitCommand("BulkFormatCommand", () => AkmlSql.Shell.Shared.Productivity.BulkFormatCommand.Initialize(this, commandService));
                TryInitCommand("BookmarkCommands", () => AkmlSql.Shell.Shared.Navigation.BookmarkCommands.Initialize(this, commandService));
                TryInitCommand("SplitTableCommand", () => SplitTableCommand.Initialize(this, commandService));

                // Formatting commands
                TryInitCommand("UnformatCommand", () => UnformatCommand.Initialize(this, commandService));
            }

            // Non-critical initialization — failures must not break the extension
            try
            {
                LoggerFactory.Initialize();
                Log.Information("AKML SQL package initializing for VS 2026 (x64)");

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
                    // Spec 030 T021 / FR-006: annotate the idle status text with the active
                    // formatting style when the user has opted into it (best-effort).
                    string? activeStyle = null;
                    try
                    {
                        var fmt = AkmlSql.Core.Config.ConfigManager.Load().Formatter;
                        if (fmt.ShowProfileInStatusBar) activeStyle = fmt.ActiveProfile;
                    }
                    catch (Exception cfgEx) { Log.Debug(cfgEx, "Status-bar active-style annotation: config load failed (using version only)"); }
                    StatusBarManager.SetLoaded(statusBar, activeStyle);
                }

                UpdateLauncher.LaunchIfDue();

                // Launch Engine process for IntelliSense, formatting, analysis
                System.Threading.Tasks.Task.Run(() => EngineLifecycle.LaunchAsync());

                ExecutionCapture.Initialize(this);
                ExecutionInterceptor.Initialize(this);
                TabManagementInitializer.Initialize(this);
                TransactionMonitor.Initialize(this);
                AiSettingsValidator.Initialize();

                Log.Information("AKML SQL package initialized successfully for VS 2026");
            }
            catch (Exception ex)
            {
                try { Log.Error(ex, "AKML SQL non-critical init failed for VS 2026"); } catch { /* Intentional: logger may not be initialized */ }

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
        /// Invokes a single command's Initialize call. Catches and logs any exception so that a
        /// failure in one command does not prevent subsequent commands from registering.
        /// </summary>
        private static void TryInitCommand(string commandName, Action init)
        {
            try
            {
                init();
            }
            catch (Exception ex)
            {
                try { Log.Warning(ex, "Command registration failed for {CommandName} — skipping (non-fatal)", commandName); } catch { /* Intentional: logger may not be initialized */ }
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
