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
                AboutCommand.Initialize(this, commandService);
                CheckUpdateCommand.Initialize(this, commandService);
                OptionsCommand.Initialize(this, commandService);
                FormatStylesCommand.Initialize(this, commandService);
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

                // Phase 8 US7 — Go to Definition & Peek Definition
                GoToDefinitionCommand.Initialize(this, commandService);
                PeekDefinitionCommand.Initialize(this, commandService);

                // Phase 8 US12 — Object Search & Find References
                ObjectSearchCommand.Initialize(this, commandService);
                FindReferencesCommand.Initialize(this, commandService);

                // Phase 8 US2 — Grid Copy/Export commands
                GridContextMenuWiring.RegisterCommands(commandService);

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

                // Formatting commands
                UnformatCommand.Initialize(this, commandService);
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
