using System;
using System.Security;
using AkmlSql.Core;
using Serilog;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AkmlSql.Updater
{
    /// <summary>
    /// Windows notifications from the updater.
    /// <para>
    /// The "ready to install" notification's buttons act through the <c>akmlsql-update:</c> URL
    /// scheme the installer registers, so no COM activator is needed: "Install now" runs
    /// <c>AkmlSql.Updater.exe --install</c>, and clicking the notification itself opens the release
    /// notes. Windows only shows a notification from an unpackaged app whose AppUserModelID is on a
    /// Start-menu shortcut — the installer's "Check for AKML SQL updates" shortcut carries
    /// <see cref="Constants.AppUserModelId"/>.
    /// </para>
    /// </summary>
    internal sealed class UpdateToast : IUpdateNotifications
    {
        internal const string InstallArgument = "install";
        internal const string DetailsArgument = "details";

        public bool Ready(string version) => TryShow(ReadyXml(version), $"ready v{version}");

        public void UpToDate(string currentVersion) => TryShow(
            MessageXml("AKML SQL is up to date", $"You have the latest version, {Escape(currentVersion)}."),
            "up to date");

        public void CouldNotCheck() => TryShow(
            MessageXml("Couldn't check for AKML SQL updates",
                "The update server could not be reached. Check your internet connection and try again."),
            "check failed");

        public void CouldNotDownload(string version) => TryShow(
            MessageXml($"AKML SQL {Escape(version)} couldn't be downloaded",
                "The download did not complete or did not match its published checksum. It will be tried again later."),
            "download failed");

        /// <summary>Shows one notification. Returns false (and logs why) when Windows refuses.</summary>
        private static bool TryShow(string xmlText, string what)
        {
            try
            {
                var xml = new XmlDocument();
                xml.LoadXml(xmlText);

                // Show() first: reading the notifier's Setting before an app has ever shown a
                // notification throws "Element not found" even though Show() would have worked.
                ToastNotificationManager.CreateToastNotifier(Constants.AppUserModelId).Show(
                    new ToastNotification(xml) { Tag = "update", Group = "akmlsql" });
                Log.Information("Update notification shown: {What}", what);
                return true;
            }
            catch (Exception ex)
            {
                // Typical causes: notifications turned off for AKML SQL, focus assist, or the
                // Start-menu shortcut missing. The IDE's startup prompt still offers the update.
                Log.Warning(ex, "Could not show the update notification ({What})", what);
                return false;
            }
        }

        /// <summary>The "ready to install" notification. Internal for tests; the version is escaped.</summary>
        internal static string ReadyXml(string version)
        {
            var scheme = Constants.UpdateProtocolScheme;
            return
                $"<toast launch=\"{scheme}:{DetailsArgument}\" activationType=\"protocol\">" +
                "<visual><binding template=\"ToastGeneric\">" +
                $"<text>AKML SQL {Escape(version)} is ready to install</text>" +
                "<text>The update has been downloaded and checked. Installing closes SSMS and Visual Studio if they are open.</text>" +
                "</binding></visual>" +
                "<actions>" +
                $"<action content=\"Install now\" activationType=\"protocol\" arguments=\"{scheme}:{InstallArgument}\"/>" +
                "<action content=\"Later\" activationType=\"system\" arguments=\"dismiss\"/>" +
                "</actions>" +
                "</toast>";
        }

        /// <summary>A plain two-line notification. Both lines must already be escaped.</summary>
        internal static string MessageXml(string title, string body) =>
            "<toast><visual><binding template=\"ToastGeneric\">" +
            $"<text>{title}</text><text>{body}</text>" +
            "</binding></visual></toast>";

        private static string Escape(string text) => SecurityElement.Escape(text) ?? string.Empty;
    }
}
