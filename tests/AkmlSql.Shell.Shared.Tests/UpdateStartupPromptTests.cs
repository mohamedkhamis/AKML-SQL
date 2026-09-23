#nullable enable
using System;
using System.Linq;
using System.Windows.Controls;
using AkmlSql.Core.Update;
using AkmlSql.Shell.Shared.Update;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The one-time offer at IDE startup of an update the scheduled task (or an earlier check)
    /// already downloaded and verified. Until this existed, nothing ever read the background
    /// check's result — the user only heard about an update by clicking Check for Updates.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class UpdateStartupPromptTests
    {
        private const string Installed = "1.26.0923.0723";

        private static UpdateResult Ready(string version = "1.26.1001.0900") => new()
        {
            Available = true,
            Version = version,
            DownloadState = UpdateDownloadStates.Verified,
            VerifiedInstallerPath = @"C:\cache\AKMLSQLSetup-" + version + ".exe",
        };

        private static bool Offers(UpdateResult? result, bool fileThere = true) =>
            UpdateStartupPrompt.ShouldPrompt(result, _ => fileThere, Installed);

        [Fact]
        public void AReadyNewerUpdate_IsOffered()
        {
            Assert.True(Offers(Ready()));
        }

        [Fact]
        public void ItIsOfferedOnce()
        {
            var result = Ready();
            result.ShellPromptedAt = DateTimeOffset.UtcNow;

            Assert.False(Offers(result));
        }

        [Fact]
        public void TheVersionAlreadyInstalled_IsNotOffered()
        {
            // Right after the update installs, the offer file still describes it.
            Assert.False(Offers(Ready(Installed)));
        }

        [Fact]
        public void ADownloadThatIsNotVerified_IsNotOffered()
        {
            var result = Ready();
            result.DownloadState = UpdateDownloadStates.Downloading;

            Assert.False(Offers(result));
        }

        [Fact]
        public void AnInstallerThatWasDeleted_IsNotOffered()
        {
            Assert.False(Offers(Ready(), fileThere: false));
        }

        [Fact]
        public void NoOffer_NoPrompt()
        {
            Assert.False(Offers(null));
            Assert.False(Offers(new UpdateResult { Available = false }));
        }

        [StaFact]
        public void TheStartupOffer_SaysLater_NotCancel()
        {
            var dlg = UpdateInstallConfirmDialog.CreateForUpdate("1.26.1001.0900", declineText: "Later");

            var decline = LogicalTree.Descendants<Button>(dlg).Single(b => b.IsCancel);

            Assert.Equal("Later", (string)decline.Content);
        }
    }
}
