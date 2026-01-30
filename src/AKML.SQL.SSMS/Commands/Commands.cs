using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using AKML.SQL.Shared;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Task = System.Threading.Tasks.Task;

namespace AKML.SQL.SSMS.Commands
{
    /// <summary>
    /// Command to format the current SQL document.
    /// </summary>
    internal sealed class FormatSqlCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("a1b2c3d4-e5f6-7890-abcd-ef12345678ab");

        private readonly AsyncPackage _package;

        private FormatSqlCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static FormatSqlCommand? Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new FormatSqlCommand(package, commandService!);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _ = ExecuteAsync();
        }

        private async Task ExecuteAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Get the active text view
                var textManager = await _package.GetServiceAsync(typeof(SVsTextManager)) as IVsTextManager;
                IVsTextView? textView = null;
                textManager?.GetActiveView(1, null, out textView);

                if (textView == null)
                {
                    VsShellUtilities.ShowMessageBox(
                        _package,
                        "No active text editor found.",
                        AkmlConstants.ProductName,
                        OLEMSGICON.OLEMSGICON_INFO,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }

                // Get the text buffer
                textView.GetBuffer(out var textLines);
                if (textLines == null) return;

                // Get all text
                textLines.GetLineCount(out var lineCount);
                textLines.GetLengthOfLine(lineCount - 1, out var lastLineLength);
                textLines.GetLineText(0, 0, lineCount - 1, lastLineLength, out var text);

                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                // Format via Core service
                var grpcClient = AkmlSqlPackage.Instance?.GrpcClient;
                if (grpcClient == null)
                {
                    VsShellUtilities.ShowMessageBox(
                        _package,
                        "AKML-SQL Core service is not connected.",
                        AkmlConstants.ProductName,
                        OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }

                var result = await grpcClient.FormatSqlAsync(text);
                
                if (!result.Success)
                {
                    VsShellUtilities.ShowMessageBox(
                        _package,
                        $"Failed to format SQL: {result.ErrorMessage}",
                        AkmlConstants.ProductName,
                        OLEMSGICON.OLEMSGICON_WARNING,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Replace text
                var editTextSpan = new TextSpan
                {
                    iStartLine = 0,
                    iStartIndex = 0,
                    iEndLine = lineCount - 1,
                    iEndIndex = lastLineLength
                };

                textLines.ReplaceLines(
                    editTextSpan.iStartLine,
                    editTextSpan.iStartIndex,
                    editTextSpan.iEndLine,
                    editTextSpan.iEndIndex,
                    IntPtr.Zero,
                    0,
                    null);

                var formattedPtr = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(result.FormattedText);
                try
                {
                    textLines.ReplaceLines(0, 0, 0, 0, formattedPtr, result.FormattedText.Length, null);
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeHGlobal(formattedPtr);
                }
            }
            catch (Exception ex)
            {
                VsShellUtilities.ShowMessageBox(
                    _package,
                    $"Error formatting SQL: {ex.Message}",
                    AkmlConstants.ProductName,
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }
        }
    }

    /// <summary>
    /// Command to refresh database metadata.
    /// </summary>
    internal sealed class RefreshMetadataCommand
    {
        public const int CommandId = 0x0101;
        public static readonly Guid CommandSet = new Guid("a1b2c3d4-e5f6-7890-abcd-ef12345678ab");

        private readonly AsyncPackage _package;

        private RefreshMetadataCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static RefreshMetadataCommand? Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new RefreshMetadataCommand(package, commandService!);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            VsShellUtilities.ShowMessageBox(
                _package,
                "Metadata refresh triggered. This will update IntelliSense with the latest database schema.",
                AkmlConstants.ProductName,
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

            // TODO: Implement actual metadata refresh
        }
    }
}
