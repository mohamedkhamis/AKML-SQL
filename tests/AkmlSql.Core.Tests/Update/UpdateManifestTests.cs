using Xunit;
using AkmlSql.Core.Update;

namespace AkmlSql.Core.Tests.Update
{
    public class UpdateManifestTests
    {
        [Fact]
        public void Defaults_AreEmptyStrings()
        {
            var m = new UpdateManifest();
            Assert.Equal(string.Empty, m.Version);
            Assert.Equal(string.Empty, m.DownloadUrl);
            Assert.Equal(string.Empty, m.ReleaseNotesUrl);
            Assert.Equal(string.Empty, m.MinimumOsVersion);
            Assert.Equal(string.Empty, m.Sha256Hash);
        }

        [Fact]
        public void Properties_CanBeSetAndRead()
        {
            var m = new UpdateManifest
            {
                Version = "2.0.0",
                DownloadUrl = "https://example.com/setup.exe",
                ReleaseNotesUrl = "https://example.com/notes",
                MinimumOsVersion = "10.0.19041",
                Sha256Hash = "deadbeef"
            };
            Assert.Equal("2.0.0", m.Version);
            Assert.Equal("https://example.com/setup.exe", m.DownloadUrl);
            Assert.Equal("https://example.com/notes", m.ReleaseNotesUrl);
            Assert.Equal("10.0.19041", m.MinimumOsVersion);
            Assert.Equal("deadbeef", m.Sha256Hash);
        }
    }
}
