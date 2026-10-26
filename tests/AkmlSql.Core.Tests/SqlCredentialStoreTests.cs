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

        // Spec 043 follow-up (review): Save says whether the password is now kept, so a caller never reports a password as
        // remembered when the store file could not be written.
        [Fact]
        public void Save_ReturnsTrue_WhenWritten_AndFalse_ForAMissingKey()
        {
            var server = "unit-test-srv-5"; var login = "sa";
            try
            {
                Assert.True(SqlCredentialStore.Save(server, login, "x"));
                Assert.False(SqlCredentialStore.Save(string.Empty, login, "x"));
                Assert.False(SqlCredentialStore.Save(server, string.Empty, "x"));
            }
            finally { SqlCredentialStore.Remove(server, login); }
        }

        [Fact]
        public void Save_ReturnsFalse_AndKeepsNothing_WhenTheStoreFileCannotBeWritten()
        {
            InIsolatedStore(breakWrites =>
            {
                breakWrites();

                Assert.False(SqlCredentialStore.Save("unit-test-srv-6", "sa", "x"));
                Assert.False(SqlCredentialStore.Has("unit-test-srv-6", "sa"));
            });
        }

        // Spec 043 follow-up (review): Remove says whether the entry is gone, so "forget" is never reported for a password the
        // store still holds.
        [Fact]
        public void Remove_ReturnsTrue_WhenTheRemovalIsWritten_OrThereIsNothingToRemove()
        {
            var server = "unit-test-srv-7"; var login = "sa";
            try
            {
                SqlCredentialStore.Save(server, login, "x");
                Assert.True(SqlCredentialStore.Remove(server, login));
                Assert.False(SqlCredentialStore.Has(server, login));

                Assert.True(SqlCredentialStore.Remove(server, login));            // nothing left to remove
                Assert.True(SqlCredentialStore.Remove(string.Empty, login));      // a missing key names nothing
            }
            finally { SqlCredentialStore.Remove(server, login); }
        }

        [Fact]
        public void Remove_ReturnsFalse_AndKeepsTheEntry_WhenTheStoreFileCannotBeWritten()
        {
            InIsolatedStore(breakWrites =>
            {
                Assert.True(SqlCredentialStore.Save("unit-test-srv-8", "sa", "x"));   // the store file exists first
                breakWrites();

                Assert.False(SqlCredentialStore.Remove("unit-test-srv-8", "sa"));
                Assert.True(SqlCredentialStore.TryGet("unit-test-srv-8", "sa", out var pwd));
                Assert.Equal("x", pwd);
            });
        }

        // Spec 043 follow-up (review): one password per server and login is kept by saving one key and removing the others in
        // one write, so two saves for the same login under different keys cannot each delete the other's fresh entry.
        [Fact]
        public void Save_WithServersToRemove_ReplacesThemForThatLoginOnly_InOneWrite()
        {
            var keyA = "unit-test-srv-9a"; var keyB = "unit-test-srv-9b";
            try
            {
                SqlCredentialStore.Save(keyA, "sa", "old");
                SqlCredentialStore.Save(keyA, "bob", "bobs");

                Assert.True(SqlCredentialStore.Save(keyB, "sa", "new", keyA));

                Assert.False(SqlCredentialStore.Has(keyA, "sa"));
                Assert.True(SqlCredentialStore.TryGet(keyB, "sa", out var pwd));
                Assert.Equal("new", pwd);
                Assert.True(SqlCredentialStore.Has(keyA, "bob"));                 // another login under the removed key is kept
                Assert.True(SqlCredentialStore.Save(keyB, "sa", "again", null!, string.Empty));   // no key to remove is not an error
                Assert.True(SqlCredentialStore.TryGet(keyB, "sa", out pwd));
                Assert.Equal("again", pwd);
            }
            finally
            {
                SqlCredentialStore.Remove(keyA, "sa");
                SqlCredentialStore.Remove(keyA, "bob");
                SqlCredentialStore.Remove(keyB, "sa");
            }
        }

        [Fact]
        public void Save_WithServersToRemove_ReturnsFalse_AndChangesNothing_WhenTheStoreFileCannotBeWritten()
        {
            InIsolatedStore(breakWrites =>
            {
                Assert.True(SqlCredentialStore.Save("unit-test-srv-10a", "sa", "old"));
                breakWrites();

                Assert.False(SqlCredentialStore.Save("unit-test-srv-10b", "sa", "new", "unit-test-srv-10a"));
                Assert.True(SqlCredentialStore.TryGet("unit-test-srv-10a", "sa", out var pwd));
                Assert.Equal("old", pwd);
                Assert.False(SqlCredentialStore.Has("unit-test-srv-10b", "sa"));
            });
        }

        [Fact]
        public void Concurrent_saves_that_replace_each_other_always_leave_exactly_one_entry()
        {
            InIsolatedStore(_ =>
            {
                System.Threading.Tasks.Parallel.For(0, 40, i =>
                {
                    var (mine, other) = i % 2 == 0 ? ("unit-test-srv-11a", "unit-test-srv-11b") : ("unit-test-srv-11b", "unit-test-srv-11a");
                    Assert.True(SqlCredentialStore.Save(mine, "sa", "pw" + i, other));
                });

                var kept = SqlCredentialStore.List().FindAll(e => e.Login == "sa");
                Assert.Single(kept);
            });
        }

        // Spec 043 follow-up (review, round 3): a store that cannot be read is never written. It used to read as empty, so Has
        // said no, Remove said "nothing to remove" (a forget answered as done) and Save wrote a list with only the new entry —
        // dropping every other credential, the SSMS extension's too.
        [Fact]
        public void Save_And_Remove_ReturnFalse_AndWriteNothing_WhileTheStoreCannotBeRead()
        {
            InIsolatedStorePath(storePath =>
            {
                Assert.True(SqlCredentialStore.Save("unit-test-srv-12a", "sa", "a-pw"));
                Assert.True(SqlCredentialStore.Save("unit-test-srv-12b", "bob", "b-pw"));   // another login, as the SSMS extension's

                using (new System.IO.FileStream(storePath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                {
                    Assert.False(SqlCredentialStore.Save("unit-test-srv-12c", "sa", "c-pw"));
                    Assert.False(SqlCredentialStore.Remove("unit-test-srv-12a", "sa"));
                    Assert.False(SqlCredentialStore.Has("unit-test-srv-12a", "sa"));             // lookups answer "not found"
                    Assert.False(SqlCredentialStore.TryGet("unit-test-srv-12a", "sa", out _));
                    Assert.Empty(SqlCredentialStore.List());
                    Assert.False(System.IO.File.Exists(storePath + ".tmp"));                      // no write was even attempted
                }

                Assert.True(SqlCredentialStore.TryGet("unit-test-srv-12a", "sa", out var a));
                Assert.Equal("a-pw", a);
                Assert.True(SqlCredentialStore.TryGet("unit-test-srv-12b", "bob", out var b));
                Assert.Equal("b-pw", b);
                Assert.False(SqlCredentialStore.Has("unit-test-srv-12c", "sa"));
            });
        }

        [Fact]
        public void A_store_that_is_not_valid_JSON_reads_as_empty_and_the_next_save_heals_it()
        {
            InIsolatedStorePath(storePath =>
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(storePath)!);
                System.IO.File.WriteAllText(storePath, "{ this is not [ a credential list");

                Assert.False(SqlCredentialStore.Has("unit-test-srv-13", "sa"));
                Assert.Empty(SqlCredentialStore.List());
                Assert.True(SqlCredentialStore.Remove("unit-test-srv-13", "sa"));              // nothing in it to remove

                Assert.True(SqlCredentialStore.Save("unit-test-srv-13", "sa", "x"));
                Assert.True(SqlCredentialStore.TryGet("unit-test-srv-13", "sa", out var pwd));
                Assert.Equal("x", pwd);
                Assert.Single(SqlCredentialStore.List());
            });
        }

        [Fact]
        public void A_null_entry_in_the_store_is_skipped()
        {
            InIsolatedStorePath(storePath =>
            {
                Assert.True(SqlCredentialStore.Save("unit-test-srv-14", "sa", "x"));
                var json = System.IO.File.ReadAllText(storePath).TrimEnd();
                System.IO.File.WriteAllText(storePath, json.Substring(0, json.Length - 1) + ", null ]");

                Assert.True(SqlCredentialStore.TryGet("unit-test-srv-14", "sa", out var pwd));
                Assert.Equal("x", pwd);
                Assert.False(SqlCredentialStore.Has("unit-test-srv-15", "sa"));
                Assert.Single(SqlCredentialStore.List());
            });
        }

        /// <summary>
        /// Runs <paramref name="test"/> against a store in an isolated AppData root (<c>AKML_APP_DATA_ROOT</c>), handing it an
        /// action that makes every later write fail: a directory in the place of the temp file breaks the atomic write.
        /// </summary>
        private static void InIsolatedStore(System.Action<System.Action> test) =>
            InIsolatedStorePath(storePath => test(() => System.IO.Directory.CreateDirectory(storePath + ".tmp")));

        /// <summary>Runs <paramref name="test"/> against a store in an isolated AppData root, handing it the store file's path.</summary>
        private static void InIsolatedStorePath(System.Action<string> test)
        {
            const string rootVariable = "AKML_APP_DATA_ROOT";
            var priorRoot = System.Environment.GetEnvironmentVariable(rootVariable);
            var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "akmlsql-cred-test-" + System.Guid.NewGuid().ToString("N"));
            System.Environment.SetEnvironmentVariable(rootVariable, tempRoot);
            try
            {
                var storePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Constants.ConfigFilePath)!, "sql-credentials.json");
                test(storePath);
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(rootVariable, priorRoot);
                try { System.IO.Directory.Delete(tempRoot, recursive: true); }
                catch (System.IO.IOException) { /* left for %TEMP% cleanup */ }
            }
        }
    }
}
