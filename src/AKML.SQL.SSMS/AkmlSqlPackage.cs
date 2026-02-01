using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using AKML.SQL.Shared;
using AKML.SQL.SSMS.Services;
using AKML.SQL.SSMS.UI.Settings;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;
using Task = System.Threading.Tasks.Task;

namespace AKML.SQL.SSMS
{
    /// <summary>
    /// AKML-SQL VSIX Package
    /// This is the main entry point for the SSMS extension.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    // Note: Options pages disabled for SSMS compatibility
    // [ProvideOptionPage(typeof(AkmlOptionsPage), "AKML-SQL", "General", 0, 0, true)]
    // [ProvideOptionPage(typeof(AkmlAdvancedOptionsPage), "AKML-SQL", "Advanced", 0, 0, true)]
    // Note: Menu resource disabled - no .vsct file
    // [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class AkmlSqlPackage : AsyncPackage
    {
        public const string PackageGuidString = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        private CoreProcessManager? _processManager;
        private GrpcClientService? _grpcClient;
        private SettingsService? _settingsService;
        private ILogger? _logger;

        /// <summary>
        /// Gets the singleton instance of the package.
        /// </summary>
        public static AkmlSqlPackage? Instance { get; private set; }

        /// <summary>
        /// Gets the gRPC client service.
        /// </summary>
        public GrpcClientService? GrpcClient => _grpcClient;

        /// <summary>
        /// Gets the settings service.
        /// </summary>
        public SettingsService? SettingsService => _settingsService;

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;

            // Initialize logging
            InitializeLogging();

            _logger?.Information("Initializing {ProductName} v{Version}", 
                AkmlConstants.ProductName, AkmlConstants.ProductVersion);

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                // Initialize settings service
                progress.Report(new ServiceProgressData("Loading settings..."));
                _settingsService = SettingsService.Instance;
                _logger?.Information("Settings loaded. IntelliSense enabled: {Enabled}",
                    _settingsService.Settings.EnableIntelliSense);

                // Start Core process
                progress.Report(new ServiceProgressData("Starting AKML-SQL Core service..."));
                await StartCoreServiceAsync(cancellationToken);

                // Initialize gRPC client
                progress.Report(new ServiceProgressData("Connecting to Core service..."));
                await InitializeGrpcClientAsync(cancellationToken);

                // Register commands (disabled for now - requires .vsct)
                // progress.Report(new ServiceProgressData("Registering commands..."));
                // await Commands.FormatSqlCommand.InitializeAsync(this);
                // await Commands.RefreshMetadataCommand.InitializeAsync(this);

                _logger?.Information("{ProductName} initialized successfully", AkmlConstants.ProductName);

                // Show welcome message
                ShowWelcomeMessage();
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error initializing {ProductName}", AkmlConstants.ProductName);
                ShowErrorMessage($"Failed to initialize {AkmlConstants.ProductName}: {ex.Message}");
            }
        }

        private void InitializeLogging()
        {
            var logPath = Path.Combine(AkmlConstants.Paths.LogFolder, "ssms-.log");
            Directory.CreateDirectory(AkmlConstants.Paths.LogFolder);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            _logger = Log.Logger;
        }

        private async Task StartCoreServiceAsync(CancellationToken cancellationToken)
        {
            _processManager = new CoreProcessManager(_logger!);
            
            var corePath = GetCoreExecutablePath();
            if (!File.Exists(corePath))
            {
                throw new FileNotFoundException(
                    $"Core service executable not found at: {corePath}. Please ensure AKML.SQL.Core is built and deployed.");
            }

            await _processManager.StartAsync(corePath, cancellationToken);
        }

        private async Task InitializeGrpcClientAsync(CancellationToken cancellationToken)
        {
            _grpcClient = new GrpcClientService(_logger!);
            await _grpcClient.ConnectAsync(cancellationToken);

            // Verify connection with ping
            var pingResult = await _grpcClient.PingAsync(cancellationToken);
            if (!pingResult.Success)
            {
                throw new InvalidOperationException("Failed to connect to Core service");
            }

            _logger?.Information("Connected to Core service v{Version}", pingResult.ServerVersion);
        }

        private string GetCoreExecutablePath()
        {
            // Look for Core executable in several locations
            var assemblyLocation = Path.GetDirectoryName(GetType().Assembly.Location) ?? string.Empty;
            
            var searchPaths = new[]
            {
                // Same directory as VSIX
                Path.Combine(assemblyLocation, AkmlConstants.CoreExecutableName),
                // Subdirectory
                Path.Combine(assemblyLocation, "Core", AkmlConstants.CoreExecutableName),
                // AppData
                Path.Combine(AkmlConstants.Paths.AppDataFolder, AkmlConstants.CoreExecutableName),
                // Development path
                Path.Combine(assemblyLocation, "..", "..", "..", "AKML.SQL.Core", "bin", "Debug", "net8.0", AkmlConstants.CoreExecutableName)
            };

            foreach (var path in searchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    _logger?.Debug("Found Core executable at: {Path}", fullPath);
                    return fullPath;
                }
            }

            return searchPaths[0]; // Return first path for error message
        }

        private void ShowWelcomeMessage()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var outputWindow = GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                if (outputWindow != null)
                {
                    var paneGuid = VSConstants.GUID_OutWindowGeneralPane;
                    outputWindow.GetPane(ref paneGuid, out var pane);
                    
                    if (pane == null)
                    {
                        outputWindow.CreatePane(ref paneGuid, "General", 1, 1);
                        outputWindow.GetPane(ref paneGuid, out pane);
                    }

                    pane?.OutputString($"\n{AkmlConstants.ProductName} v{AkmlConstants.ProductVersion} loaded successfully.\n");
                    pane?.OutputString($"© {DateTime.Now.Year} {AkmlConstants.CompanyName}\n\n");
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(ex, "Could not show welcome message");
            }
        }

        private void ShowErrorMessage(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            VsShellUtilities.ShowMessageBox(
                this,
                message,
                AkmlConstants.ProductName,
                OLEMSGICON.OLEMSGICON_WARNING,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logger?.Information("Shutting down {ProductName}", AkmlConstants.ProductName);

                // TextSynchronizer is MEF-managed and disposes itself
                TextSynchronizer.Instance?.Dispose();
                _grpcClient?.Dispose();
                _processManager?.Dispose();
                _settingsService?.Dispose();

                Log.CloseAndFlush();
            }

            base.Dispose(disposing);
        }
    }
}
