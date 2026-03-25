# Implementation Plan: AI-Powered SQL Assistance

**Branch**: `009-ai-sql-assistance` | **Date**: 2026-03-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/009-ai-sql-assistance/spec.md`

## Summary

Phase 9 integrates AI into the deterministic foundation built by Phases 2–8. It adds 7 AI features (text-to-SQL, explain, fix, optimize, index suggestions, chat panel, inline ghost text) via a multi-model provider architecture supporting 7 provider types (Anthropic, OpenAI, Azure OpenAI, Gemini, Ollama, LM Studio, custom). All features are opt-in, never auto-execute, and support 5 privacy modes (full, schemaOnly, anonymous, offline, disabled). The technical approach uses `Microsoft.Extensions.AI` (`IChatClient` abstraction) for provider-agnostic AI calls, DPAPI for API key encryption, AST-based privacy redaction, and streaming responses over the existing named pipe IPC.

## Technical Context

**Language/Version**: C# / .NET 10 (Engine), .NET Framework 4.7.2 (Shell), netstandard2.0 (Core shared library)
**Primary Dependencies**: Microsoft.Extensions.AI 10.4.1, Anthropic.SDK 5.10.0, OllamaSharp 5.4.25, OpenAI 2.9.1, Mscc.GenerativeAI 3.1.0, Microsoft.ML.Tokenizers 2.0.0
**Storage**: `%AppData%/AKML SQL/config.json` (AiSettings section with DPAPI-encrypted API keys)
**Testing**: xunit 2.x, Microsoft.NET.Test.Sdk 17.x, `dotnet test`
**Target Platform**: Windows (SSMS 20/21/22, VS 2019/2022/2026) — desktop IDE extensions
**Project Type**: IDE extension (out-of-process engine + in-process shell)
**Performance Goals**: Text-to-SQL < 5s (cloud) / < 15s (local), Explain < 3s, Ghost text < 500ms, Schema context prep < 200ms
**Constraints**: Opt-in only; zero auto-execution; privacy modes enforced; offline-capable via local models; DPAPI encryption for keys
**Scale/Scope**: Databases with up to 1000+ tables; 500 object max per AI request; 7 provider types; 6 IDE targets

## Constitution Check

*No constitution file exists. Gate passes by default.*

**Post-Phase 1 re-check**: Design follows all existing codebase conventions:
- Out-of-process engine pattern (AI providers run in Engine, not Shell)
- MessagePack IPC with `[MessagePackObject]` POCOs
- Shared `.projitems` pattern for 6-target shell compilation
- `AppSettings` POCO pattern for configuration
- `OleMenuCommand` pattern for VS SDK commands
- `ToolWindowPane` pattern for dockable panels
- DPAPI pattern (already used in `HistoryEncryption.cs`)

## Project Structure

### Documentation (this feature)

```text
specs/009-ai-sql-assistance/
├── plan.md              # This file
├── research.md          # Phase 0 output — technology decisions
├── data-model.md        # Phase 1 output — entity definitions
├── quickstart.md        # Phase 1 output — architecture & build guide
├── contracts/
│   ├── ai-ipc.md        # Phase 1 output — IPC message contracts
│   └── ai-settings.md   # Phase 1 output — configuration contract
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
  AkmlSql.Core/                           # Shared library (netstandard2.0 + net10.0)
    Config/
      AppSettings.cs                       # + AiSettings nested class
    Ipc/
      RpcMessage.cs                        # + Message types 70-78, 170-178
      Messages/
        Ai*.cs                             # 18 new request/response POCOs
        ChatTurnDto.cs
        AnnotationDto.cs
        IndexSuggestionDto.cs
        CodeActionDto.cs
    Models/
      Ai/
        SchemaContext.cs                   # Schema context model
        SchemaObjectSummary.cs
        ColumnSummary.cs
        PrivacyTransformation.cs

  AkmlSql.Engine/                          # Out-of-process .NET 10 engine
    Ai/
      AiRequestHandler.cs                 # Main dispatcher (routes by request type)
      Providers/
        AiProviderFactory.cs              # IChatClient factory
        GeminiChatClientAdapter.cs        # Thin adapter for Gemini SDK
      Context/
        SchemaContextBuilder.cs           # Builds compressed schema from cache
        SchemaContextFormatter.cs         # Compact DDL-like format
        TokenEstimator.cs                 # Token count estimation
      Privacy/
        LiteralRedactor.cs               # AST-based literal replacement
        IdentifierHasher.cs              # HMAC-based name hashing
        PrivacyTransformer.cs            # Orchestrates redaction pipeline
      Prompts/
        PromptTemplates.cs               # All prompt templates
      Streaming/
        StreamCoalescer.cs               # Batch stream chunks
      Security/
        CredentialManager.cs             # DPAPI encrypt/decrypt
    Server/
      PipeRpcServer.cs                    # + AI dispatch cases

  AkmlSql.Shell.Shared/                    # Shared project for all 6 targets
    Commands/
      TextToSqlCommand.cs                 # Ctrl+Shift+G
      AiExplainCommand.cs                 # Ctrl+Shift+E
      AiFixCommand.cs                     # Shift+Alt+R
      AiOptimizeCommand.cs                # Ctrl+Shift+O
      AiChatPanelCommand.cs               # Ctrl+Shift+A
    Ai/
      DiffPreviewPanel.cs                 # WPF diff view
      ExplanationPanel.cs                 # Structured explanation view
      AiChatToolWindow.cs                 # ToolWindowPane
      AiChatPanel.cs                      # WPF chat UI
      GhostTextAdornment.cs              # Inline editor adornment
      GhostTextAdornmentProvider.cs      # MEF provider
      AiResponseHandler.cs               # Response → UI mapping
    Dialogs/
      SettingsDialog.cs                   # + AI settings tab
    PackageGuids.cs                       # + AI command IDs (0x0700+)

  AkmlSql.Ssms20..VS2026/                 # 6 target projects
    AkmlSqlPackage.cs                     # + AI command registration
    AkmlSqlXxx.vsct                       # + AI buttons + keybindings

tests/
  AkmlSql.Core.Tests/
    Ai/
      SchemaContextBuilderTests.cs
      LiteralRedactorTests.cs
      IdentifierHasherTests.cs
      PrivacyTransformerTests.cs
      TokenEstimatorTests.cs
      PromptTemplateTests.cs
```

**Structure Decision**: Follows the existing multi-project architecture. AI provider logic is entirely in the Engine (out-of-process, .NET 10). AI UI components are in Shell.Shared (in-process, .NET Framework 4.7.2, compiled against 6 VS SDK versions). Core IPC messages and models are in the shared Core library (netstandard2.0). No new projects are needed — all new code fits into existing project boundaries.

## Complexity Tracking

No constitution violations. The design adds no new projects, no new abstraction layers beyond what the provider SDKs already provide (IChatClient), and follows all existing patterns.
