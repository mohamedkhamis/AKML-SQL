using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Productivity
{
    /// <summary>
    /// Spec 030 T087 (FR-046) — "Bulk Format…" command. Opens the existing
    /// <see cref="BulkFormatWizard"/> (populated with the on-disk formatting profiles), and on
    /// <see cref="DialogResult.OK"/> dispatches a <see cref="BulkFormatRequest"/> to the engine
    /// (<c>MessageTypes.BulkFormat</c>), then reports the resulting
    /// <see cref="BulkFormatReportResponse"/> summary. Modeled on
    /// <see cref="AkmlSql.Shell.Shared.Analysis.ManageRulesCommand"/>; the engine already runs the
    /// batch (<c>BulkFormatHandler</c> → <c>FormatRequestHandler.HandleBulkFormatAsync</c>).
    /// Surfaced on the AKML menu and the Command Palette on both hosts.
    /// </summary>
    internal sealed class BulkFormatCommand
    {
        private BulkFormatCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdBulkFormat);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static BulkFormatCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new BulkFormatCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                using var wizard = new BulkFormatWizard(GetAvailableProfiles());
                if (wizard.ShowDialog() != DialogResult.OK)
                    return;

                if (wizard.SelectedFiles.Count == 0)
                    return;

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    MessageBox.Show("The AKML SQL engine is not running yet — try again in a moment.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var request = new BulkFormatRequest
                {
                    SessionId     = Guid.NewGuid().ToString("N"),
                    FilePaths     = wizard.SelectedFiles.ToArray(),
                    ProfileName   = wizard.SelectedProfile,
                    DryRun        = wizard.PreviewOnly,
                    CreateBackups = wizard.CreateBackups,
                };

                // Dispatch on a background task so the (potentially multi-minute) batch does not
                // block the UI thread — mirrors FormatDocumentCommand. Report the summary back on
                // the UI thread once the engine responds.
                bool previewOnly = wizard.PreviewOnly;
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var response = await client.SendRequestAsync<BulkFormatReportResponse, BulkFormatRequest>(
                            MessageTypes.BulkFormat, request, timeoutMs: 300_000);

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        if (response == null)
                        {
                            MessageBox.Show("Bulk format did not return a result from the engine.",
                                Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            return;
                        }

                        Log.Information(
                            "BulkFormat: {Total} file(s) — {Success} formatted, {Failed} failed, {Skipped} skipped in {Elapsed} ms",
                            response.TotalFiles, response.SuccessCount, response.FailedCount, response.SkippedCount, response.ElapsedMs);

                        var verb = previewOnly ? "would be formatted" : "formatted";
                        MessageBox.Show(
                            $"Bulk format complete.\n\n" +
                            $"Total files: {response.TotalFiles}\n" +
                            $"{char.ToUpperInvariant(verb[0]) + verb.Substring(1)}: {response.SuccessCount}\n" +
                            $"Failed: {response.FailedCount}\n" +
                            $"Skipped: {response.SkippedCount}\n" +
                            $"Elapsed: {response.ElapsedMs} ms",
                            Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "BulkFormat dispatch failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BulkFormatCommand.Execute failed");
                MessageBox.Show("Bulk Format failed: " + ex.Message,
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Returns the formatting profile names for the wizard's dropdown — the file names (sans
        /// extension) of the <c>*.akmlstyle</c> profiles under <c>%AppData%/AKML SQL/profiles</c>,
        /// always including "Default". Mirrors <c>EditProfileCommand</c>'s profiles directory.
        /// </summary>
        private static string[] GetAvailableProfiles()
        {
            var names = new List<string> { "Default" };
            try
            {
                var profilesDir = Path.Combine(Constants.AppDataPath, "profiles");
                if (Directory.Exists(profilesDir))
                {
                    foreach (var file in Directory.GetFiles(profilesDir, "*.akmlstyle"))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        if (!string.IsNullOrWhiteSpace(name) &&
                            !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            names.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "BulkFormat: could not enumerate formatting profiles");
            }
            return names.ToArray();
        }
    }
}
