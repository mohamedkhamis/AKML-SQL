# Quickstart: AI-Powered SQL Assistance

**Date**: 2026-03-25
**Branch**: `009-ai-sql-assistance`

## Architecture Overview

Phase 9 adds AI capabilities to the existing Shell ↔ Engine architecture. AI provider communication happens entirely in the Engine process (.NET 10), while the Shell handles UI (commands, tool windows, adornments).

```
┌──────────────────────────────────────────────────┐
│  Shell (.NET Framework 4.7.2, VS/SSMS process)   │
│                                                    │
│  Commands:                                         │
│    TextToSqlCommand (Ctrl+Shift+G)                 │
│    AiExplainCommand (Ctrl+Shift+E)                 │
│    AiFixCommand (Shift+Alt+R)                      │
│    AiOptimizeCommand (Ctrl+Shift+O)                │
│    AiChatPanelCommand (Ctrl+Shift+A)               │
│                                                    │
│  UI Components:                                    │
│    DiffPreviewPanel (diff view for AI suggestions) │
│    ExplanationPanel (structured explanation view)  │
│    AiChatToolWindow (dockable chat panel)          │
│    GhostTextAdornment (inline editor adornment)    │
│                                                    │
│  PipeRpcClient → Named Pipe                        │
└───────────────────┬──────────────────────────────┘
                    │ MessagePack frames
┌───────────────────┴──────────────────────────────┐
│  Engine (.NET 10, self-contained, win-x64)        │
│                                                    │
│  PipeRpcServer                                     │
│    ├── AiRequestHandler (dispatcher)               │
│    │     ├── SchemaContextBuilder                  │
│    │     ├── PrivacyTransformer                    │
│    │     └── AiProviderFactory → IChatClient       │
│    │           ├── AnthropicProvider               │
│    │           ├── OpenAiProvider                   │
│    │           ├── AzureOpenAiProvider              │
│    │           ├── GeminiProvider                   │
│    │           ├── OllamaProvider                   │
│    │           ├── LmStudioProvider                 │
│    │           └── CustomEndpointProvider           │
│    │                                               │
│    └── Existing handlers (completion, format, ...)  │
│                                                    │
│  SchemaCacheManager (reused from Phase 2)          │
│  TsqlParserService (reused from Phase 2)           │
│  CredentialManager (DPAPI encryption)              │
└──────────────────────────────────────────────────┘
```

## Key Packages

| Package | Version | Layer | Purpose |
|---------|---------|-------|---------|
| Microsoft.Extensions.AI | 10.4.1 | Engine | IChatClient abstraction + middleware |
| Microsoft.Extensions.AI.OpenAI | 10.4.1 | Engine | OpenAI/Azure/LM Studio/Custom adapter |
| Anthropic.SDK | 5.10.0 | Engine | Anthropic Claude |
| OllamaSharp | 5.4.25 | Engine | Ollama local models |
| Mscc.GenerativeAI | 3.1.0 | Engine | Google Gemini |
| System.Security.Cryptography.ProtectedData | 10.0.5 | Engine | DPAPI key encryption |
| Microsoft.ML.Tokenizers | 2.0.0 | Engine | Token count estimation |

## Source Code Layout

```text
src/
  AkmlSql.Core/
    Config/
      AppSettings.cs                    # Add AiSettings section
    Ipc/
      RpcMessage.cs                     # Add message types 70-78, 170-178
      Messages/
        AiTextToSqlRequest.cs           # [MessagePackObject] request POCOs
        AiTextToSqlResponse.cs          # [MessagePackObject] response POCOs
        AiExplainRequest.cs
        AiExplainResponse.cs
        AiFixRequest.cs
        AiFixResponse.cs
        AiOptimizeRequest.cs
        AiOptimizeResponse.cs
        AiIndexAnalysisRequest.cs
        AiIndexAnalysisResponse.cs
        AiChatRequest.cs
        AiChatResponse.cs
        AiGhostTextRequest.cs
        AiGhostTextResponse.cs
        AiProviderTestRequest.cs
        AiProviderTestResponse.cs
        AiStreamChunkMessage.cs
        AiStreamCancelRequest.cs
        ChatTurnDto.cs
        AnnotationDto.cs
        IndexSuggestionDto.cs
        CodeActionDto.cs
    Models/
      Ai/
        SchemaContext.cs                # Schema context for AI requests
        SchemaObjectSummary.cs
        ColumnSummary.cs
        PrivacyTransformation.cs

  AkmlSql.Engine/
    Ai/
      AiRequestHandler.cs              # Main dispatcher for all AI requests
      Providers/
        AiProviderFactory.cs           # Creates IChatClient from config
        GeminiChatClientAdapter.cs     # Thin IChatClient adapter for Gemini
      Context/
        SchemaContextBuilder.cs        # Builds compressed schema context
        SchemaContextFormatter.cs      # Formats schema as compact DDL
        TokenEstimator.cs              # Token count estimation
      Privacy/
        LiteralRedactor.cs             # AST-based literal replacement
        IdentifierHasher.cs            # HMAC-based name hashing
        PrivacyTransformer.cs          # Orchestrates redaction pipeline
      Prompts/
        PromptTemplates.cs             # Prompt templates for each AI feature
        TextToSqlPrompt.cs
        ExplainPrompt.cs
        FixPrompt.cs
        OptimizePrompt.cs
        IndexAnalysisPrompt.cs
        ChatSystemPrompt.cs
        GhostTextPrompt.cs
      Streaming/
        StreamCoalescer.cs             # Batches stream chunks (50ms / 5 tokens)
      Security/
        CredentialManager.cs           # DPAPI encrypt/decrypt for API keys

  AkmlSql.Shell.Shared/
    Commands/
      TextToSqlCommand.cs              # Ctrl+Shift+G handler
      AiExplainCommand.cs              # Ctrl+Shift+E handler
      AiFixCommand.cs                  # Shift+Alt+R handler
      AiOptimizeCommand.cs             # Ctrl+Shift+O handler
      AiChatPanelCommand.cs            # Ctrl+Shift+A handler
    Ai/
      DiffPreviewPanel.cs              # WPF diff view (original vs AI suggestion)
      ExplanationPanel.cs              # WPF structured explanation view
      AiChatToolWindow.cs              # ToolWindowPane for chat
      AiChatPanel.cs                   # WPF chat UI control
      GhostTextAdornment.cs            # Editor inline ghost text
      GhostTextAdornmentProvider.cs    # MEF provider for ghost text
      AiResponseHandler.cs             # Processes AI responses for UI display
    Dialogs/
      SettingsDialog.cs                # Add AI settings tab

  AkmlSql.Ssms20/ (and Ssms21, Ssms22, VS2019, VS2022, VS2026)
    AkmlSqlPackage.cs                  # Register AI commands
    AkmlSqlXxx.vsct                    # Add AI command buttons + keybindings

tests/
  AkmlSql.Core.Tests/
    Ai/
      SchemaContextBuilderTests.cs
      LiteralRedactorTests.cs
      IdentifierHasherTests.cs
      PrivacyTransformerTests.cs
      PromptTemplateTests.cs
      TokenEstimatorTests.cs
```

## Implementation Order

1. **Infrastructure** (Weeks 1-2): AiSettings, credential encryption, provider factory, IChatClient abstraction, IPC messages, provider test command
2. **Schema Context** (Week 2): SchemaContextBuilder, formatter, token estimator, privacy transformer
3. **Text-to-SQL + Explain** (Weeks 3-4): Prompt templates, diff preview UI, explanation panel, streaming
4. **Fix + Optimize** (Weeks 5-6): Error capture integration, annotation system, index suggestion extraction
5. **Index Analysis + Chat** (Weeks 7-8): Execution plan parsing, chat tool window, conversation management
6. **Ghost Text** (Weeks 9-10): Adornment layer, debounce logic, IntelliSense coordination
7. **Local Models + Offline** (Weeks 11-12): Ollama/LM Studio testing, offline fallback, model management
8. **QA + Polish** (Weeks 13-14): Accuracy testing, prompt refinement, cross-target testing

## Build Commands

```bash
# Engine (includes new AI packages)
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# Shell (each target separately with MSBuild)
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal

# Tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```
