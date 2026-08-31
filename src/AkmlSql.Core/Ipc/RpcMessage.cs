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
    /// <para>
    /// <b>Spec 014 reservation</b> (SQL Prompt parity, 2026-04-09): the integer ranges
    /// <c>90..99</c> (shell→engine requests) and <c>190..199</c> (engine→shell responses)
    /// are reserved for spec-014 features that did not exist before. The first three
    /// allocations are <see cref="FindInvalidObjects"/> (90), <see cref="FindUnusedVariables"/> (91)
    /// and <see cref="EncryptedObjectDecryption"/> (92). Most other spec-014 features
    /// reuse pre-existing message types from the previous Phase 7/8/9 work
    /// (e.g. <see cref="DocumentOutline"/>, <see cref="ScriptAs"/>, <see cref="GridExport"/>,
    /// <see cref="AiExplain"/>, <see cref="AiIndexAnalysis"/>, <see cref="AiTextToSql"/>,
    /// <see cref="SafetyCheck"/>, <see cref="RequestRefactorPreview"/>).
    /// </para>
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

        // Shell → Engine (Spec 030 T052: Manage Rules dialog — request the full rule catalog.
        //   Pairs with response 133.)
        public const int ListAnalysisRules = 33;

        // Shell → Engine (Wildcard Expansion)
        public const int WildcardExpansion = 27;

        // Shell → Engine (Spec 020: Format Styles editor schema descriptor)
        //   The editor UI requests the canonical FormatSettingSchema (groups + settings + types
        //   + defaults + ranges) so it can build its tree from one source of truth.
        //   See specs/020-sqlprompt-visual-parity/contracts/ipc-style-editor-schema.md
        public const int RequestStyleEditorSchema = 28;

        // Shell → Engine (Spec 020 T031: Format Styles editor "Export to SQL Prompt" button)
        //   Asks the engine to write the named profile as a .sqlpromptstylev2 XML file at the
        //   given absolute path via SqlPromptExporter.ExportToFile. Pairs with response 129.
        public const int ProfileExportSqlPrompt = 29;

        // Engine → Shell (Code Analysis)
        public const int AnalysisResult = 125;

        // Engine → Shell (Spec 030 T052: ListAnalysisRules result — pairs with request 33)
        public const int ListAnalysisRulesResult = 133;

        // Engine → Shell (Wildcard Expansion)
        public const int WildcardExpansionResult = 127;

        // Engine → Shell (Spec 020: Format Styles editor schema descriptor — pairs with RequestStyleEditorSchema=28)
        public const int StyleEditorSchemaResult = 128;

        // Engine → Shell (Spec 020 T031: ProfileExportSqlPrompt result — pairs with request 29)
        public const int ProfileExportSqlPromptResult = 129;

        // Engine → Shell (Spec 030 T020: DuplicateProfile result — pairs with request 32)
        public const int DuplicateProfileResult = 132;

        // Shell → Engine (Spec 030 T020: Format Styles editor New/Copy — server-side duplicate of
        //   a stored profile by name via ProfileManager.Duplicate. Pairs with response 132.)
        public const int DuplicateProfile = 32;

        // Shell → Engine (Spec 033: Format Styles editor load-on-select — read one stored profile.
        //   Returns the .akmlstyle file text VERBATIM (never re-serialized: serialization bumps
        //   metadata.modified and drops unknown nested fields) plus the directory-derived
        //   read-only flag. Pairs with response 134.)
        public const int ProfileGet = 34;

        // Engine → Shell (Spec 033: ProfileGet result — pairs with request 34)
        public const int ProfileGetResult = 134;

        // Shell → Engine (Spec 033: Format Styles editor Rename — atomic engine-side rename of a
        //   CUSTOM profile: file name + JSON metadata.name + the .source.json import sidecar move
        //   in one transaction. Never touches config.json — updating Formatter.ActiveProfile
        //   after renaming the active style is the shell caller's job. Pairs with response 135.)
        public const int ProfileRename = 35;

        // Engine → Shell (Spec 033: ProfileRename result — pairs with request 35)
        public const int ProfileRenameResult = 135;

        // Shell → Engine (Session rule suppression — the "Disable RULE for this session" quick fix
        //   and the Manage Rules dialog's session strip. Adds/removes/clears/lists the rules held
        //   in the engine's in-memory SessionSuppressionStore, which nothing persists: the scope
        //   ends when the engine process does. Pairs with response 136.)
        public const int SessionSuppression = 36;

        // Engine → Shell (SessionSuppression result — pairs with request 36)
        public const int SessionSuppressionResult = 136;

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

        // Shell → Engine (Schema loading status poll)
        public const int SchemaStatusRequest = 80;

        // Shell → Engine (Spec 014: SQL Prompt parity)
        // — see the class XML doc for the reservation policy on the 90..99 range.
        public const int FindInvalidObjects = 90;
        public const int FindUnusedVariables = 91;
        public const int EncryptedObjectDecryption = 92;

        // Shell → Engine (Spec 029: SQL-auth credential validation)
        public const int TestSqlConnection = 93;

        // Web/Shell → Engine (Spec 030: Connect dialog database dropdown — enumerate sys.databases)
        public const int ListDatabases = 94;

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

        // Engine → Shell (Schema loading status response)
        public const int SchemaStatusResponse = 180;

        // Engine → Shell (Spec 014 responses)
        public const int FindInvalidObjectsResult = 190;
        public const int FindUnusedVariablesResult = 191;
        public const int EncryptedObjectDecryptionResult = 192;

        // Engine → Shell (Spec 029)
        public const int TestSqlConnectionResult = 193;

        // Engine → Web/Shell (Spec 030: Connect dialog database dropdown)
        public const int ListDatabasesResult = 194;

        // Spec 021 (web edition) M3 — WebSocket bridge handshake.
        // See specs/021-web-edition/contracts/rpc-handshake.md.
        public const int HandshakeRequest = 200;
        public const int HandshakeResponse = 201;

        // Spec 021 (web edition) M5 — schema-cache identity protocol.
        // Browser asks the engine for the canonical (server, database) pair used as the
        // IndexedDB cache key. See specs/021-web-edition/contracts/schema-cache-shape.md.
        public const int SchemaIdentifyRequest = 202;
        public const int SchemaIdentifyResponse = 203;

        // Spec 021 (web edition) M5 — schema-cache change detection.
        // Browser polls engine every 30s with a (server, db) tuple; engine returns the
        // CHECKSUM_AGG(BINARY_CHECKSUM(...)) result over sys.objects. Browser compares
        // to its cached checksum and triggers a Phase A refresh on drift.
        public const int SchemaChecksumRequest = 204;
        public const int SchemaChecksumResponse = 205;

        // Spec 021 (web edition) — close-out of the matrix-test gap.
        // Cancellation signal for in-flight AI streaming requests. Engine drops the
        // associated CancellationTokenSource so the streaming handler bails.
        public const int AiStreamCancelResult = 179;

        // Spec 021 (web edition) — M2 diagnostics export extension.
        // Browser asks the engine for the last N KB of engine.log to append to its
        // diagnostics ZIP (FR-005a). Capability-gated on
        // `diagnostics.engine-log-tail.v1`.
        public const int EngineLogTailRequest = 206;
        public const int EngineLogTailResponse = 207;

        // Spec 021 (web edition) — M5 task T109. Cache-backed completion fallback.
        // Browser fetches per-phase snapshots of the engine's DatabaseCache so it
        // can serve IntelliSense from IndexedDB while the bridge is unreachable.
        // Phase A carries schemas + object names (light, sub-500 ms target). Phase B
        // adds columns + foreign keys. Both responses ship the MessagePack-serialised
        // SchemaPhasePayload in a byte[] field rather than a base64 string — the IPC
        // wire is already binary MessagePack so base64 would only add overhead.
        public const int SchemaPhaseARequest = 208;
        public const int SchemaPhaseAResponse = 209;
        public const int SchemaPhaseBRequest = 210;
        public const int SchemaPhaseBResponse = 211;

        // Spec 030 — Phase 5 (web edition) query execution + virtualized results grid + inline CRUD.
        // Kept in the 200+ web-bridge band (212+ was free; the named-pipe shell does not use these).
        //   • ExecuteQuery (212) → ExecuteQueryResult (213): run a SELECT/batch on the persistent
        //     per-session SqlConnection and stream back row data (SAFE string?[][] encoding) + per-column
        //     provenance for CRUD eligibility.
        //   • ExecuteCancel (214): NOTIFICATION (RequestId=0, no paired result) — signal the per-session
        //     CancellationTokenSource for a QueryId (cancels a queued execute; mirrors AiStreamCancel=78).
        //   • ApplyChanges (215) → ApplyChangesResult (216): commit grid edits (parameterized
        //     UPDATE/INSERT/DELETE) inside one transaction on the SAME persistent connection.
        public const int ExecuteQuery = 212;
        public const int ExecuteQueryResult = 213;
        public const int ExecuteCancel = 214;
        public const int ApplyChanges = 215;
        public const int ApplyChangesResult = 216;
    }
}
