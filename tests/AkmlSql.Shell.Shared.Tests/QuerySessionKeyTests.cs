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

    /// <summary>
    /// Finding 6 (PR #249 review): the saved/unsaved decision must be answerable from DTE state
    /// alone (no filesystem I/O), and it must stay unit-testable as a pure function.
    /// </summary>
    public class ExecutionCaptureIsSavedToDiskTests
    {
        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData(@"C:\Reports\", true)]
        [InlineData(@"\\server\share\Reports\", true)]
        public void IsSavedToDisk_reflects_whether_the_document_has_an_on_disk_path(string? path, bool expected)
            => Assert.Equal(expected, ExecutionCapture.IsSavedToDisk(path));
    }

    /// <summary>
    /// Finding 7 (PR #249 review): a save of an INACTIVE tab must be a complete no-op for session-
    /// key tracking — it must not update <c>_lastActiveDocumentPath</c>, and it must not retire or
    /// migrate another tab's session key.
    /// </summary>
    /// <remarks>
    /// Every path used here is unique to THIS class (not reused from <see cref="QuerySessionKeyTests"/>
    /// or elsewhere in this file/assembly): <see cref="DocumentSessionKeys"/>'s backing dictionary is
    /// process-static, so a collision on a shared literal path across test classes running in the
    /// same process could contaminate results regardless of xunit's parallelization settings.
    /// </remarks>
    public class ApplyDocumentSavedTests
    {
        [Fact]
        public void Save_of_the_active_document_migrates_its_own_key_and_updates_tracking()
        {
            var oldPath = @"C:\f7temp\f7-scratch-active.sql";
            var newPath = @"C:\f7Reports\f7-ActiveSave.sql";
            var key = DocumentSessionKeys.ForDocument(oldPath);

            var result = ExecutionCapture.ApplyDocumentSaved(
                lastActiveDocumentPath: oldPath, activePath: newPath, newPath: newPath);

            Assert.Equal(newPath, result);
            Assert.Equal(key, DocumentSessionKeys.ForDocument(newPath));   // migrated, not re-minted
        }

        [Fact]
        public void Save_of_an_inactive_document_does_not_change_the_tracked_path()
        {
            var activeTabPath = @"C:\f7temp\f7-tabA-1.sql";
            var inactiveTabPath = @"C:\f7temp\f7-tabB-1.sql";

            var result = ExecutionCapture.ApplyDocumentSaved(
                lastActiveDocumentPath: activeTabPath, activePath: activeTabPath, newPath: inactiveTabPath);

            Assert.Equal(activeTabPath, result);   // tracking still points at the ACTIVE tab, not the saved one
        }

        /// <summary>
        /// Regression test for the exact Finding 7 sequence: Save-All fires DocumentSaved for tab A
        /// (active) then tab B (not active); the user then saves A again. Tab B's session key must
        /// survive unchanged — no migration onto it, no retirement via the collision-decline path.
        /// </summary>
        [Fact]
        public void SaveAll_then_saving_the_active_tab_again_does_not_retire_the_inactive_tabs_key()
        {
            var pathA = @"C:\f7temp\f7-tabA-2.sql";
            var pathB = @"C:\f7temp\f7-tabB-2.sql";
            DocumentSessionKeys.ForDocument(pathA);
            var keyB = DocumentSessionKeys.ForDocument(pathB);

            string? lastActive = pathA;   // A is the active tab throughout this sequence

            // Save-All: DocumentSaved(A) -- A is active, a same-path no-op here.
            lastActive = ExecutionCapture.ApplyDocumentSaved(lastActive, activePath: pathA, newPath: pathA);
            // Save-All: DocumentSaved(B) -- B is NOT active; must not hijack tracking.
            lastActive = ExecutionCapture.ApplyDocumentSaved(lastActive, activePath: pathA, newPath: pathB);

            Assert.Equal(pathA, lastActive);   // still A, not corrupted to B by B's save

            // The user now saves A again (still active, still the same path) -- must stay a no-op,
            // not a Rename(B, A) triggered by a stale tracked path.
            lastActive = ExecutionCapture.ApplyDocumentSaved(lastActive, activePath: pathA, newPath: pathA);

            Assert.Equal(keyB, DocumentSessionKeys.ForDocument(pathB));   // B's key survives untouched
        }

        [Fact]
        public void Same_path_save_of_the_active_document_is_a_no_op()
        {
            var path = @"C:\f7Reports\f7-SamePath.sql";
            var key = DocumentSessionKeys.ForDocument(path);

            var result = ExecutionCapture.ApplyDocumentSaved(
                lastActiveDocumentPath: path, activePath: path, newPath: path);

            Assert.Equal(path, result);
            Assert.Equal(key, DocumentSessionKeys.ForDocument(path));
        }
    }
}
