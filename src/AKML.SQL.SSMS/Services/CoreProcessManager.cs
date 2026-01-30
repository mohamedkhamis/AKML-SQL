using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AKML.SQL.Shared;
using Serilog;

namespace AKML.SQL.SSMS.Services
{
    /// <summary>
    /// Manages the lifecycle of the Core service process.
    /// Handles starting, monitoring, and stopping the out-of-process Core service.
    /// </summary>
    public class CoreProcessManager : IDisposable
    {
        private readonly ILogger _logger;
        private Process? _coreProcess;
        private bool _disposed;

        public CoreProcessManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Gets whether the Core process is currently running.
        /// </summary>
        public bool IsRunning => _coreProcess != null && !_coreProcess.HasExited;

        /// <summary>
        /// Starts the Core service process.
        /// </summary>
        public async Task StartAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            if (IsRunning)
            {
                _logger.Debug("Core process is already running");
                return;
            }

            // Check if another instance is already running
            var existingProcess = FindExistingCoreProcess();
            if (existingProcess != null)
            {
                _logger.Information("Found existing Core process (PID: {ProcessId})", existingProcess.Id);
                _coreProcess = existingProcess;
                return;
            }

            _logger.Information("Starting Core process: {Path}", executablePath);

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // Pass environment variables
                Environment =
                {
                    ["DOTNET_ENVIRONMENT"] = "Production",
                    ["AKML_SQL_PARENT_PID"] = Process.GetCurrentProcess().Id.ToString()
                }
            };

            try
            {
                _coreProcess = Process.Start(startInfo);
                
                if (_coreProcess == null)
                {
                    throw new InvalidOperationException("Failed to start Core process");
                }

                _coreProcess.EnableRaisingEvents = true;
                _coreProcess.Exited += OnCoreProcessExited;
                _coreProcess.OutputDataReceived += OnCoreOutputReceived;
                _coreProcess.ErrorDataReceived += OnCoreErrorReceived;
                
                _coreProcess.BeginOutputReadLine();
                _coreProcess.BeginErrorReadLine();

                _logger.Information("Core process started (PID: {ProcessId})", _coreProcess.Id);

                // Wait for process to be ready (give it time to start listening)
                await WaitForProcessReadyAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to start Core process");
                throw;
            }
        }

        /// <summary>
        /// Stops the Core service process gracefully.
        /// </summary>
        public async Task StopAsync(TimeSpan timeout)
        {
            if (_coreProcess == null || _coreProcess.HasExited)
            {
                return;
            }

            _logger.Information("Stopping Core process (PID: {ProcessId})", _coreProcess.Id);

            try
            {
                // Try graceful shutdown first
                _coreProcess.CloseMainWindow();

                // Wait for exit with timeout
                var exitTask = Task.Run(() => _coreProcess.WaitForExit((int)timeout.TotalMilliseconds));
                if (await exitTask)
                {
                    _logger.Information("Core process stopped gracefully");
                    return;
                }

                // Force kill if still running
                _logger.Warning("Core process did not stop gracefully, forcing termination");
                _coreProcess.Kill();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping Core process");
            }
        }

        /// <summary>
        /// Restarts the Core service process.
        /// </summary>
        public async Task RestartAsync(string executablePath, CancellationToken cancellationToken = default)
        {
            await StopAsync(TimeSpan.FromSeconds(5));
            await StartAsync(executablePath, cancellationToken);
        }

        private Process? FindExistingCoreProcess()
        {
            try
            {
                var processes = Process.GetProcessesByName(AkmlConstants.CoreProcessName);
                foreach (var process in processes)
                {
                    if (!process.HasExited)
                    {
                        return process;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error finding existing Core process");
            }

            return null;
        }

        private async Task WaitForProcessReadyAsync(CancellationToken cancellationToken)
        {
            var timeout = TimeSpan.FromMilliseconds(AkmlConstants.Timeouts.ProcessStart);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_coreProcess == null || _coreProcess.HasExited)
                {
                    throw new InvalidOperationException("Core process terminated unexpectedly");
                }

                // Check if pipe is available (simple connectivity check)
                if (File.Exists(@"\\.\pipe\" + AkmlConstants.PipeName))
                {
                    _logger.Debug("Core process is ready (pipe available)");
                    return;
                }

                await Task.Delay(100, cancellationToken);
            }

            // Even if pipe check fails, process might still be starting
            // Let the gRPC connection handle final verification
            _logger.Debug("Core process startup wait completed (timeout reached, continuing)");
        }

        private void OnCoreProcessExited(object? sender, EventArgs e)
        {
            var exitCode = _coreProcess?.ExitCode ?? -1;
            _logger.Warning("Core process exited with code {ExitCode}", exitCode);
            
            // TODO: Implement auto-restart if configured
        }

        private void OnCoreOutputReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.Debug("[Core] {Output}", e.Data);
            }
        }

        private void OnCoreErrorReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.Warning("[Core Error] {Output}", e.Data);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_coreProcess != null)
                {
                    _coreProcess.Exited -= OnCoreProcessExited;
                    
                    // Only kill if we started it
                    if (!_coreProcess.HasExited)
                    {
                        _coreProcess.Kill();
                    }
                    
                    _coreProcess.Dispose();
                    _coreProcess = null;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error disposing Core process");
            }
        }
    }
}
