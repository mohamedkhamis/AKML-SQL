#nullable enable
using System;
using System.ComponentModel.Design;
using System.Windows.Forms;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// Spec 030 T067 (FR-022) — "Script as ALTER" for the programmable object under the caret.
    /// Resolves the object name, sends a <c>ScriptAs</c> request with TemplateType "ALTER" (the engine
    /// fetches the live sys.sql_modules definition and rewrites the leading CREATE → ALTER), and opens
    /// the result in a new editor tab. Not a refactor-preview op — mirrors CrudGeneration/ScriptAs.
    /// </summary>
    internal sealed class ScriptAsAlterCommand
    {
        private ScriptAsAlterCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdScriptAsAlter);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static ScriptAsAlterCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new ScriptAsAlterCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var ctx = RefactorCommandHelper.TryGetActiveEditor();
                if (ctx == null)
                {
                    MessageBox.Show("Open a SQL document and place the cursor on an object name.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var (schema, name) = RefactorCommandHelper.ExtractObjectAtCaret(ctx.DocumentText, ctx.CaretOffset);
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("Place the cursor on a procedure, view, function or trigger name.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    MessageBox.Show("The AKML SQL engine is not running yet — try again in a moment.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ScriptAsResponse? response = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    response = await client.SendRequestAsync<ScriptAsResponse, ScriptAsRequest>(
                        MessageTypes.ScriptAs,
                        new ScriptAsRequest
                        {
                            SessionId    = ctx.SessionId,
                            SchemaName   = schema,
                            ObjectName   = name,
                            TemplateType = "ALTER"
                        },
                        timeoutMs: 15_000);
                });

                if (response == null || !response.Success || string.IsNullOrEmpty(response.Sql))
                {
                    MessageBox.Show("Script as ALTER failed: " + (response?.Error ?? "no response from the engine."),
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                OpenInNewTab(response.Sql!, response.FullObjectName ?? $"{schema}.{name}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ScriptAsAlterCommand.Execute failed");
                MessageBox.Show("Script as ALTER failed: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void OpenInNewTab(string sql, string objectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null) return;

                var safe = objectName.Replace(".", "_").Replace("[", "").Replace("]", "");
                foreach (var bad in System.IO.Path.GetInvalidFileNameChars()) safe = safe.Replace(bad, '_');
                var unique = System.IO.Path.GetRandomFileName().Substring(0, 6);
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ALTER_{safe}_{unique}.sql");
                System.IO.File.WriteAllText(path, sql, System.Text.Encoding.UTF8);

                dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindCode);
                Log.Information("ScriptAsAlterCommand: opened ALTER script for {Object}", objectName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ScriptAsAlterCommand: failed to open script tab; copying to clipboard");
                try { System.Windows.Clipboard.SetText(sql); } catch { /* last resort */ }
            }
        }
    }
}
