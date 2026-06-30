#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.VisualStudio.Shell;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>Spec 030 T067 — Inline a dynamic EXEC('...') / sp_executesql into its query (FR-020).</summary>
    internal sealed class InlineExecCommand
    {
        private InlineExecCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdInlineExec);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static InlineExecCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new InlineExecCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
            => RefactorCommandHelper.RunInlineRefactor((int)RefactorOperationType.InlineExec, "Inline EXEC", "Apply");
    }
}
