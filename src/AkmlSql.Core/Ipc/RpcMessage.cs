using MessagePack;

namespace AkmlSql.Core.Ipc
{
    [MessagePackObject]
    public class RpcMessage
    {
        [Key(0)]
        public int MessageType { get; set; }

        [Key(1)]
        public int RequestId { get; set; }

        [Key(2)]
        public byte[]? Payload { get; set; }
    }

    public static class MessageTypes
    {
        // Shell → Engine
        public const int ConnectionChanged = 1;
        public const int DocumentChanged = 2;
        public const int RequestCompletion = 3;
        public const int RequestSignatureHelp = 4;
        public const int RequestQuickInfo = 5;
        public const int SchemaRefreshRequest = 6;
        public const int Ping = 7;
        public const int Shutdown = 8;

        // Shell → Engine (Formatter)
        public const int FormatDocument = 10;
        public const int FormatSelection = 11;
        public const int FormatPreview = 12;
        public const int FormatAction = 13;
        public const int ProfileList = 14;
        public const int ProfileSave = 15;
        public const int ProfileDelete = 16;
        public const int ProfileImport = 17;
        public const int BulkFormat = 18;
        public const int BulkFormatCancel = 19;

        // Engine → Shell
        public const int CompletionResult = 101;
        public const int SignatureHelpResult = 102;
        public const int QuickInfoResult = 103;
        public const int SchemaRefreshComplete = 104;
        public const int Pong = 105;
        public const int Error = 106;

        // Engine → Shell (Formatter)
        public const int FormatDocumentResult = 110;
        public const int FormatSelectionResult = 111;
        public const int FormatPreviewResult = 112;
        public const int FormatActionResult = 113;
        public const int ProfileListResult = 114;
        public const int ProfileSaveResult = 115;
        public const int ProfileDeleteResult = 116;
        public const int ProfileImportResult = 117;
        public const int BulkFormatResult = 118;
    }
}
