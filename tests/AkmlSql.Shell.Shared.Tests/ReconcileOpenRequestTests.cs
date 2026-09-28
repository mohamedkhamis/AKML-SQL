#nullable enable
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T058, HIS-02) — the startup request that tells the engine which queries this SSMS
    /// has open: only query documents that have a session key.
    /// </summary>
    public class ReconcileOpenRequestTests
    {
        [Fact]
        public void Keeps_only_query_documents_with_a_key()
        {
            var request = OpenStateReporter.BuildReconcileRequest(4242, new (string, string?)[]
            {
                (@"C:\Reports\Monthly.sql", "key-file"),
                (@"C:\Users\me\AppData\Local\Temp\SQLQuery3", "key-scratch"),
                (@"C:\Reports\NotRunYet.sql", null),
                (@"C:\Projects\Readme.txt", "key-other"),
            });

            Assert.Equal(new[] { "key-file", "key-scratch" }, request.OpenSessionKeys);
        }

        [Fact]
        public void Sets_the_action_and_the_owner()
        {
            var request = OpenStateReporter.BuildReconcileRequest(4242, new (string, string?)[0]);

            Assert.Equal(HistoryActions.ReconcileOpen, request.Action);
            Assert.Equal(4242, request.OwnerPid);
        }

        [Fact]
        public void No_qualifying_document_gives_an_empty_array_not_null()
        {
            var request = OpenStateReporter.BuildReconcileRequest(1, new (string, string?)[] { (@"C:\a.txt", "k") });

            Assert.NotNull(request.OpenSessionKeys);
            Assert.Empty(request.OpenSessionKeys!);
        }
    }
}
