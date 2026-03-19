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

        // Engine → Shell
        public const int CompletionResult = 101;
        public const int SignatureHelpResult = 102;
        public const int QuickInfoResult = 103;
        public const int SchemaRefreshComplete = 104;
        public const int Pong = 105;
        public const int Error = 106;
    }
}
