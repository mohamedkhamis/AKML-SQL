#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Shell.Shared.Ai;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// T053: Opens (or brings to front) the AI Chat tool window.
    /// Bound to <see cref="CommandIds.CmdAiChatPanel"/> (0x0705).
    /// </summary>
    internal sealed class AiChatPanelCommand
    {
        private readonly Package _package;

        private AiChatPanelCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdAiChatPanel);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += AiCommandVisibility.OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        /// <summary>Singleton instance.</summary>
        public static AiChatPanelCommand? Instance { get; private set; }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// Must be called on the UI thread during package initialization.
        /// Overload for AsyncPackage (SSMS 21/22, VS 2019/2022/2026).
        /// </summary>
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            Instance = new AiChatPanelCommand(package, commandService);
        }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// Must be called on the UI thread during package initialization.
        /// Overload for synchronous Package (SSMS 20).
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new AiChatPanelCommand(package, commandService);
        }

        /// <summary>
        /// Finds or creates the <see cref="AiChatToolWindow"/> and shows it.
        /// </summary>
        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var window = _package.FindToolWindow(typeof(AiChatToolWindow), 0, create: true);
                if (window?.Frame == null)
                {
                    Log.Warning("AiChatPanelCommand: failed to create AiChatToolWindow");
                    return;
                }

                var windowFrame = (IVsWindowFrame)window.Frame;
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

                Log.Debug("AiChatPanelCommand: AI Chat tool window shown");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AiChatPanelCommand: failed to show AI Chat tool window");
            }
        }
    }
}
