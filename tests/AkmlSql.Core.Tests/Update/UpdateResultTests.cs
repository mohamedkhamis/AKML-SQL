using System;
using Xunit;
using AkmlSql.Core.Update;

namespace AkmlSql.Core.Tests.Update
{
    public class UpdateResultTests
    {
        [Fact]
        public void Defaults()
        {
            var r = new UpdateResult();
            Assert.False(r.Available);
            Assert.Equal(string.Empty, r.Version);
            Assert.Equal(string.Empty, r.DownloadUrl);
            Assert.Equal(string.Empty, r.ReleaseNotesUrl);
            Assert.Equal(default(DateTimeOffset), r.CheckedAt);
        }

        [Fact]
        public void Properties_CanBeSetAndRead()
        {
            var now = DateTimeOffset.UtcNow;
            var r = new UpdateResult
            {
                Available = true,
                Version = "1.1.0",
                DownloadUrl = "https://example.com/update.exe",
                ReleaseNotesUrl = "https://example.com/rn",
                CheckedAt = now
            };
            Assert.True(r.Available);
            Assert.Equal("1.1.0", r.Version);
            Assert.Equal("https://example.com/update.exe", r.DownloadUrl);
            Assert.Equal("https://example.com/rn", r.ReleaseNotesUrl);
            Assert.Equal(now, r.CheckedAt);
        }
    }
}
