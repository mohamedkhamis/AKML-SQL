using MessagePack;

namespace AkmlSql.Core.Ipc
{
    /// <summary>
    /// The envelope sent over the named-pipe IPC channel between the shell extension and the engine.
    /// Serialized with MessagePack. The frame wrapping this message is defined in <see cref="FrameProtocol"/>.
    /// </summary>
    [MessagePackObject]
    public class RpcMessage
    {
        /// <summary>Identifies the operation. See <see cref="MessageTypes"/> for all constants.</summary>
        [Key(0)]
        public int MessageType { get; set; }

        /// <summary>
        /// Correlates a request to its response. The engine echoes this value in the reply.
        /// Use <c>0</c> for fire-and-forget notifications that require no response.
        /// </summary>
        [Key(1)]
        public int RequestId { get; set; }

        /// <summary>MessagePack-serialized request or response POCO. The concrete type depends on <see cref="MessageType"/>.</summary>
        [Key(2)]
        public byte[]? Payload { get; set; }
    }

    /// <summary>
    /// Integer constants for <see cref="RpcMessage.MessageType"/>.
    /// Values 1–31 are sent Shell→Engine; values 101–131 are sent Engine→Shell.
    /// </summary>
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

        // Shell → Engine (Snippets)
        public const int SnippetExpand = 20;
        public const int SnippetList = 21;
        public const int SnippetSave = 22;
        public const int SnippetDelete = 23;
        public const int SnippetImport = 24;

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

        // Engine → Shell (Snippets)
        public const int SnippetExpandResult = 120;
        public const int SnippetListResult = 121;
        public const int SnippetSaveResult = 122;
        public const int SnippetDeleteResult = 123;
        public const int SnippetImportResult = 124;

        // Shell → Engine (Code Analysis)
        public const int RequestAnalyze = 25;
        public const int AnalysisSettingsChanged = 26;

        // Engine → Shell (Code Analysis)
        public const int AnalysisResult = 125;

        // Shell → Engine (Refactoring — heavyweight preview/apply)
        public const int RequestRefactorPreview = 30;
        public const int RequestRefactorApply = 31;

        // Shell → Engine (SQL History — Phase 7)
        public const int HistoryRecord = 40;
        public const int HistorySearch = 41;
        public const int HistoryAction = 42;

        // Shell → Engine (Session Recovery — Phase 7)
        public const int SessionSave = 50;
        public const int SessionRestore = 51;
        public const int SessionDelete = 52;

        // Shell → Engine (Execution Safety — Phase 7)
        public const int SafetyCheck = 55;

        // Shell → Engine (Navigation — Phase 8)
        public const int GetObjectDefinition = 60;
        public const int FindReferences = 61;
        public const int ObjectSearch = 62;

        // Shell → Engine (Editor/Productivity — Phase 8)
        public const int DocumentOutline = 64;
        public const int StatementBoundary = 65;
        public const int CrudGeneration = 66;
        public const int ScriptAs = 67;
        public const int GridExport = 68;

        // Shell → Engine (AI Assistance — Phase 9)
        public const int AiTextToSql = 70;
        public const int AiExplain = 71;
        public const int AiFix = 72;
        public const int AiOptimize = 73;
        public const int AiIndexAnalysis = 74;
        public const int AiChat = 75;
        public const int AiGhostText = 76;
        public const int AiProviderTest = 77;
        public const int AiStreamCancel = 78;

        // Engine → Shell (Refactoring)
        public const int RefactorPreviewResult = 130;
        public const int RefactorApplyResult = 131;

        // Engine → Shell (SQL History — Phase 7)
        public const int HistoryRecordResult = 140;
        public const int HistorySearchResult = 141;
        public const int HistoryActionResult = 142;

        // Engine → Shell (Session Recovery — Phase 7)
        public const int SessionSaveResult = 150;
        public const int SessionRestoreResult = 151;
        public const int SessionDeleteResult = 152;

        // Engine → Shell (Execution Safety — Phase 7)
        public const int SafetyCheckResult = 155;

        // Engine → Shell (Navigation — Phase 8)
        public const int GetObjectDefinitionResult = 160;
        public const int FindReferencesResult = 161;
        public const int ObjectSearchResult = 162;

        // Engine → Shell (Editor/Productivity — Phase 8)
        public const int DocumentOutlineResult = 164;
        public const int StatementBoundaryResult = 165;
        public const int CrudGenerationResult = 166;
        public const int ScriptAsResult = 167;
        public const int GridExportResult = 168;

        // Engine → Shell (AI Assistance — Phase 9)
        public const int AiTextToSqlResult = 170;
        public const int AiExplainResult = 171;
        public const int AiFixResult = 172;
        public const int AiOptimizeResult = 173;
        public const int AiIndexAnalysisResult = 174;
        public const int AiChatResult = 175;
        public const int AiGhostTextResult = 176;
        public const int AiProviderTestResult = 177;
        public const int AiStreamChunk = 178;
    }
}
