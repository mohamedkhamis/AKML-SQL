using Xunit;
using AkmlSql.Core.Ipc;

namespace AkmlSql.Core.Tests.Ipc
{
    public class RpcMessageTests
    {
        // ── RpcMessage ────────────────────────────────────────────────────────

        [Fact]
        public void RpcMessage_Defaults()
        {
            var m = new RpcMessage();
            Assert.Equal(0, m.MessageType);
            Assert.Equal(0, m.RequestId);
            Assert.Null(m.Payload);
        }

        [Fact]
        public void RpcMessage_PropertiesRoundTrip()
        {
            var payload = new byte[] { 1, 2, 3 };
            var m = new RpcMessage { MessageType = MessageTypes.Ping, RequestId = 42, Payload = payload };
            Assert.Equal(MessageTypes.Ping, m.MessageType);
            Assert.Equal(42, m.RequestId);
            Assert.Equal(payload, m.Payload);
        }

        // ── MessageTypes — Shell → Engine ────────────────────────────────────

        [Fact]
        public void MessageTypes_ShellToEngine()
        {
            Assert.Equal(1,  MessageTypes.ConnectionChanged);
            Assert.Equal(2,  MessageTypes.DocumentChanged);
            Assert.Equal(3,  MessageTypes.RequestCompletion);
            Assert.Equal(4,  MessageTypes.RequestSignatureHelp);
            Assert.Equal(5,  MessageTypes.RequestQuickInfo);
            Assert.Equal(6,  MessageTypes.SchemaRefreshRequest);
            Assert.Equal(7,  MessageTypes.Ping);
            Assert.Equal(8,  MessageTypes.Shutdown);
        }

        [Fact]
        public void MessageTypes_Formatter()
        {
            Assert.Equal(10, MessageTypes.FormatDocument);
            Assert.Equal(11, MessageTypes.FormatSelection);
            Assert.Equal(12, MessageTypes.FormatPreview);
            Assert.Equal(13, MessageTypes.FormatAction);
            Assert.Equal(14, MessageTypes.ProfileList);
            Assert.Equal(15, MessageTypes.ProfileSave);
            Assert.Equal(16, MessageTypes.ProfileDelete);
            Assert.Equal(17, MessageTypes.ProfileImport);
            Assert.Equal(18, MessageTypes.BulkFormat);
            Assert.Equal(19, MessageTypes.BulkFormatCancel);
        }

        [Fact]
        public void MessageTypes_Snippets()
        {
            Assert.Equal(20, MessageTypes.SnippetExpand);
            Assert.Equal(21, MessageTypes.SnippetList);
            Assert.Equal(22, MessageTypes.SnippetSave);
            Assert.Equal(23, MessageTypes.SnippetDelete);
            Assert.Equal(24, MessageTypes.SnippetImport);
        }

        [Fact]
        public void MessageTypes_EngineToShell()
        {
            Assert.Equal(101, MessageTypes.CompletionResult);
            Assert.Equal(102, MessageTypes.SignatureHelpResult);
            Assert.Equal(103, MessageTypes.QuickInfoResult);
            Assert.Equal(104, MessageTypes.SchemaRefreshComplete);
            Assert.Equal(105, MessageTypes.Pong);
            Assert.Equal(106, MessageTypes.Error);
        }

        [Fact]
        public void MessageTypes_FormatterResults()
        {
            Assert.Equal(110, MessageTypes.FormatDocumentResult);
            Assert.Equal(111, MessageTypes.FormatSelectionResult);
            Assert.Equal(112, MessageTypes.FormatPreviewResult);
            Assert.Equal(113, MessageTypes.FormatActionResult);
            Assert.Equal(114, MessageTypes.ProfileListResult);
            Assert.Equal(115, MessageTypes.ProfileSaveResult);
            Assert.Equal(116, MessageTypes.ProfileDeleteResult);
            Assert.Equal(117, MessageTypes.ProfileImportResult);
            Assert.Equal(118, MessageTypes.BulkFormatResult);
        }

        [Fact]
        public void MessageTypes_SnippetResults()
        {
            Assert.Equal(120, MessageTypes.SnippetExpandResult);
            Assert.Equal(121, MessageTypes.SnippetListResult);
            Assert.Equal(122, MessageTypes.SnippetSaveResult);
            Assert.Equal(123, MessageTypes.SnippetDeleteResult);
            Assert.Equal(124, MessageTypes.SnippetImportResult);
        }
    }
}
