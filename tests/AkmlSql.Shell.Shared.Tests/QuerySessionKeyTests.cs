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
    }
}
