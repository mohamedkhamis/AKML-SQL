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
using AkmlSql.Shell.Shared.Update;
using AkmlSql.Shell.Shared.Validation;
using Serilog;

namespace AkmlSql.Ssms22
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(Constants.ProductName, "AI-powered SQL development assistance", Constants.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad("B7B07F42-6013-4C67-A504-C771CBC7625A", PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.AkmlSqlPackageString)]
    public sealed class AkmlSqlPackage : AsyncPackage
    {
        protected override async Task InitializeAsync(
            CancellationToken cancellationToken,
            IProgress<ServiceProgressData> progress)
        {
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                LoggerFactory.Shutdown();
            }

            base.Dispose(disposing);
        }
    }
}
