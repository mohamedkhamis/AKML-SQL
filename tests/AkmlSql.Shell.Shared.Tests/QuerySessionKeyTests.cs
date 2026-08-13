using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    public class QuerySessionKeyTests
    {
        [Fact]
        public void Same_document_yields_a_stable_key()
        {
            var a = DocumentSessionKeys.ForDocument(@"C:\temp\dwnhdxfq.sql");
            var b = DocumentSessionKeys.ForDocument(@"C:\temp\dwnhdxfq.sql");
            Assert.Equal(a, b);
            Assert.False(string.IsNullOrWhiteSpace(a));
        }

        [Fact]
        public void Different_documents_yield_different_keys()
        {
            var a = DocumentSessionKeys.ForDocument(@"C:\temp\one.sql");
            var b = DocumentSessionKeys.ForDocument(@"C:\temp\two.sql");
            Assert.NotEqual(a, b);
        }

        /// <summary>Closing and reopening a file is a NEW session — that is the "one tab, one entry" rule.</summary>
        [Fact]
        public void Reopening_after_close_yields_a_new_key()
        {
            var path = @"C:\temp\reopen.sql";
            var first = DocumentSessionKeys.ForDocument(path);
            DocumentSessionKeys.Forget(path);
            var second = DocumentSessionKeys.ForDocument(path);
            Assert.NotEqual(first, second);
        }

        /// <summary>
        /// A Save (or the first Save-As of an unsaved scratch document) must NOT split one
        /// continuous editing session into two history entries: executions before and after the
        /// rename must resolve to the SAME session key.
        /// </summary>
        [Fact]
        public void Rename_preserves_the_session_key_across_a_save()
        {
            var oldPath = @"C:\temp\dwnhdxfq.sql";
            var newPath = @"C:\Reports\MonthlyReport.sql";

            var before = DocumentSessionKeys.ForDocument(oldPath);
            DocumentSessionKeys.Rename(oldPath, newPath);
            var after = DocumentSessionKeys.ForDocument(newPath);

            Assert.Equal(before, after);
        }

        /// <summary>
        /// After a rename, the OLD name must not keep the entry around — otherwise it would leak
        /// forever, and a future document that happens to reuse the old temp path would incorrectly
        /// inherit a stale session.
        /// </summary>
        [Fact]
        public void Rename_releases_the_entry_under_the_old_name()
        {
            var oldPath = @"C:\temp\abcxyz.sql";
            var newPath = @"C:\Reports\Q3Summary.sql";

            var original = DocumentSessionKeys.ForDocument(oldPath);
            DocumentSessionKeys.Rename(oldPath, newPath);

            // Asking for the old path again must mint a FRESH key (proves the old entry is gone,
            // not that a coincidentally-equal key was regenerated).
            var afterRenameOldPath = DocumentSessionKeys.ForDocument(oldPath);
            Assert.NotEqual(original, afterRenameOldPath);
        }

        /// <summary>
        /// Renaming a path with no tracked session is a safe no-op: it must not throw, and — the
        /// part a mere non-blank-key assertion cannot distinguish from a bug — it must not secretly
        /// link the two never-touched paths to the same session. Proven by asserting both paths
        /// still resolve to INDEPENDENT, distinct keys afterward (the dictionary-state proxy,
        /// since <c>Keys</c> has no public inspection surface).
        /// </summary>
        [Fact]
        public void Rename_with_no_existing_entry_is_a_no_op()
        {
            var oldPath = @"C:\temp\never-touched.sql";
            var newPath = @"C:\temp\also-never-touched.sql";

            DocumentSessionKeys.Rename(oldPath, newPath); // must not throw, must not link the two paths

            var keyForOld = DocumentSessionKeys.ForDocument(oldPath);
            var keyForNew = DocumentSessionKeys.ForDocument(newPath);

            Assert.False(string.IsNullOrWhiteSpace(keyForNew));
            Assert.NotEqual(keyForOld, keyForNew);
        }

        /// <summary>
        /// The regression test for the cross-tab merge: if a Save-As targets a path that a
        /// DIFFERENT, still-open document already owns (Tab B), migrating Tab A's key onto that
        /// path would silently fold two unrelated tabs' history into one entry. Tab B's key must
        /// survive untouched, and the two tabs' keys must remain distinct.
        /// </summary>
        [Fact]
        public void Rename_onto_a_path_owned_by_a_different_open_document_does_not_merge_sessions()
        {
            var pathB = @"C:\work\report.sql";
            var keyB = DocumentSessionKeys.ForDocument(pathB); // Tab B already owns this path

            var pathA = @"C:\temp\scratchA.sql";
            var keyA = DocumentSessionKeys.ForDocument(pathA); // Tab A, about to Save-As onto pathB

            // Tab A does Save-As -> pathB, overwriting the file Tab B has open.
            DocumentSessionKeys.Rename(pathA, pathB);

            var stillB = DocumentSessionKeys.ForDocument(pathB);
            Assert.Equal(keyB, stillB);      // Tab B's session must be untouched by the collision
            Assert.NotEqual(keyA, stillB);   // Tab A's key must NOT have overwritten Tab B's entry
        }
    }
}
