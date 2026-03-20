using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using Microsoft.VisualStudio.Shell;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    internal sealed class SendFeedbackCommand
    {
        private SendFeedbackCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdSendFeedback);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static SendFeedbackCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new SendFeedbackCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            var url = Constants.FeedbackUrl;
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                })) { }
            }
        }
    }
}
