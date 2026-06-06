using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests
{
    /// <summary>
    /// xunit collection that serializes tests touching the real %AppData%\AKML SQL directory
    /// (config.json + sql-credentials.json live there). ConfigManagerTests deletes/recreates that
    /// directory to test "create if missing"; SqlCredentialStore.Save calls Directory.CreateDirectory
    /// on the same path — so running them in parallel races. Same-collection classes never run
    /// concurrently, and DisableParallelization keeps the collection off the parallel pool entirely.
    /// </summary>
    [CollectionDefinition("AkmlSql real AppData", DisableParallelization = true)]
    public sealed class AkmlSqlRealAppDataCollection { }

    // NOTE: SqlCredentialStore persists to %AppData%\AKML SQL\sql-credentials.json. These tests
    // use a unique (server, login) per case and clean up with Remove() so they don't collide.
    [Collection("AkmlSql real AppData")]
    public class SqlCredentialStoreTests
    {
        [Fact]
        public void SaveThenTryGet_RoundTripsThePassword()
        {
            var server = "unit-test-srv-1"; var login = "sa";
            try
            {
                SqlCredentialStore.Save(server, login, "P@ss;w'd\"x");
                Assert.True(SqlCredentialStore.TryGet(server, login, out var pwd));
                Assert.Equal("P@ss;w'd\"x", pwd);
            }
            finally { SqlCredentialStore.Remove(server, login); }
        }

        [Fact]
        public void TryGet_UnknownKey_ReturnsFalse()
        {
            Assert.False(SqlCredentialStore.TryGet("no-such-srv", "no-such-login", out var pwd));
            Assert.Equal(string.Empty, pwd);
        }

        [Fact]
        public void Match_IsCaseInsensitive_OnServerAndLogin()
        {
            var server = "Unit-Test-Srv-2"; var login = "SA";
            try
            {
                SqlCredentialStore.Save(server, login, "x");
                Assert.True(SqlCredentialStore.TryGet("unit-test-srv-2", "sa", out _));
                Assert.True(SqlCredentialStore.Has("UNIT-TEST-SRV-2", "Sa"));
            }
            finally { SqlCredentialStore.Remove(server, login); }
        }

        [Fact]
        public void Remove_DeletesTheEntry()
        {
            var server = "unit-test-srv-3"; var login = "sa";
            SqlCredentialStore.Save(server, login, "x");
            SqlCredentialStore.Remove(server, login);
            Assert.False(SqlCredentialStore.TryGet(server, login, out _));
        }

        [Fact]
        public void Save_ReplacesExistingEntry_ForSameKey()
        {
            var server = "unit-test-srv-4"; var login = "sa";
            try
            {
                SqlCredentialStore.Save(server, login, "first");
                SqlCredentialStore.Save(server, login, "second");
                Assert.True(SqlCredentialStore.TryGet(server, login, out var pwd));
                Assert.Equal("second", pwd);
            }
            finally { SqlCredentialStore.Remove(server, login); }
        }
    }
}
