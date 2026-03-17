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
using AkmlSql.Core.Logging;
using AkmlSql.Shell.Shared;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.StatusBar;
using AkmlSql.Shell.Shared.Update;
using AkmlSql.Shell.Shared.Validation;
using Serilog;

namespace AkmlSql.VS2019
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(AkmlSql.Core.Constants.ProductName, "AI-powered SQL development assistance", AkmlSql.Core.Constants.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.AkmlSqlPackageString)]
    public sealed class AkmlSqlPackage : AsyncPackage
    {
        protected override async System.Threading.Tasks.Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
            IVsStatusbar statusBar = null;

            try
            {
                await base.InitializeAsync(cancellationToken, progress);

                // Initialize logging
                LoggerFactory.Initialize();
                Log.Information("AKML SQL package initializing for VS 2019 (x86)");

                // Switch to UI thread for menu registration
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // Validate installation
                var extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                LoadValidator.Validate(extensionDir);

                // Register menu commands
                var commandService = await GetServiceAsync(typeof(IMenuCommandService))
                    as Microsoft.VisualStudio.Shell.OleMenuCommandService;

                if (commandService != null)
                {
                    AboutCommand.Initialize(this, commandService);
                    CheckUpdateCommand.Initialize(this, commandService);
                    OptionsCommand.Initialize(this, commandService);
                    SendFeedbackCommand.Initialize(this, commandService);
                    ViewLogsCommand.Initialize(this, commandService);
                }

                // Set status bar
                statusBar = (IVsStatusbar)await GetServiceAsync(typeof(SVsStatusbar));
                if (statusBar != null)
                    StatusBarManager.SetLoaded(statusBar);

                // Launch background update check
                UpdateLauncher.LaunchIfDue();

                Log.Information("AKML SQL package initialized successfully for VS 2019");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AKML SQL package initialization failed for VS 2019");

                try
                {
                    if (statusBar == null)
                        statusBar = (IVsStatusbar)await GetServiceAsync(typeof(SVsStatusbar));
                    if (statusBar != null)
                        StatusBarManager.SetFailed(statusBar);
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
                LoggerFactory.Shutdown();
            base.Dispose(disposing);
        }
    }
}
