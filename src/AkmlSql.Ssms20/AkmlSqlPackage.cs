using System;
using System.ComponentModel.Design;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
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
using AkmlSql.Shell.Shared.Validation;
using Serilog;

namespace AkmlSql.Ssms20
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("AKML SQL", "AI-powered SQL development assistance", "1.0.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string)]
    [ProvideToolWindow(typeof(HistoryToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(DocumentOutlineToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(ReferencesToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideToolWindow(typeof(AiChatToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [Guid(PackageGuids.AkmlSqlPackageString)]
    public sealed class AkmlSqlPackage : Package
    {
        protected override void Initialize()
        {
            try
            {
                base.Initialize();

                // Register menu commands FIRST — before anything that might fail
                var commandService = GetService(typeof(IMenuCommandService))
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

                    // Phase 8 US4 — Execute Current Statement
                    ExecuteCurrentStatementCommand.Initialize(this, commandService);
                    ExecuteToCursorCommand.Initialize(this, commandService);

                    // Phase 8 US5 — Document Outline
                    DocumentOutlineCommand.Initialize(this, commandService);

                    // Phase 8 US8 — Navigation commands
                    NavigateStatementCommand.Initialize(this, commandService);
                    NavigateMatchingPairCommand.Initialize(this, commandService);

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

                    // Phase 9 US2 — AI Explain
                    AiExplainCommand.Initialize(this, commandService);

                    // Phase 9 US3 — AI Fix
                    AiFixCommand.Initialize(this, commandService);

                    // Phase 9 US6 — AI Chat Panel
                    AiChatPanelCommand.Initialize(this, commandService);
                }

                // Now do non-critical initialization that may fail
                try
                {
                    LoggerFactory.Initialize();
                    Log.Information("AKML SQL package initializing for SSMS 20 (x86)");

                    var extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    LoadValidator.Validate(extensionDir);

                    var statusBar = (IVsStatusbar)GetService(typeof(SVsStatusbar));
                    if (statusBar != null)
                    {
                        StatusBarManager.SetLoaded(statusBar);
                    }

                    UpdateLauncher.LaunchIfDue();
                    ExecutionCapture.Initialize(this);
                    ExecutionInterceptor.Initialize(this);
                    TabCloseMonitor.Initialize(this);
                    WindowTitleManager.Initialize(this);
                    TransactionMonitor.Initialize(this);
                    AiSettingsValidator.Initialize();

                    Log.Information("AKML SQL package initialized successfully for SSMS 20");
                }
                catch (Exception ex)
                {
                    try { Log.Error(ex, "AKML SQL non-critical initialization failed for SSMS 20"); } catch { /* Intentional: logger may not be initialized */ }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AKML SQL package initialization failed: " + ex);
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
