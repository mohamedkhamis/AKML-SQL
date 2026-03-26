#nullable enable
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine.Ai.Context;
using AkmlSql.Engine.Ai.Privacy;
using AkmlSql.Engine.Ai.Prompts;
using AkmlSql.Engine.Ai.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using MessagePack;
using Microsoft.Extensions.AI;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Ai;

/// <summary>
/// Main dispatcher for all AI assistance IPC message types (Phase 9).
/// Each handler method deserializes the request, invokes the appropriate AI pipeline,
/// and returns a serialized response. Includes privacy consent checks (T060),
/// retry with exponential backoff (T061), offline fallback (T062), and SQL validation (T063).
/// </summary>
public sealed class AiRequestHandler : IDisposable
{
    /// <summary>
    /// Providers considered local/offline that do not require privacy consent.
    /// </summary>
    private static readonly HashSet<string> LocalProviders =
        new(StringComparer.OrdinalIgnoreCase) { "ollama", "lmstudio" };

    /// <summary>Maximum backoff delay for retry logic (T061).</summary>
    private const int MaxBackoffMs = 30_000;

    private readonly SchemaContextBuilder _schemaContextBuilder;
    private readonly PrivacyTransformer _privacyTransformer;
    private readonly TsqlParserService _parserService;
    private AiSettings _aiSettings;

    public AiRequestHandler(SchemaCacheManager schemaCacheManager, TsqlParserService parserService)
    {
        ArgumentNullException.ThrowIfNull(schemaCacheManager);
        ArgumentNullException.ThrowIfNull(parserService);

        _schemaContextBuilder = new SchemaContextBuilder(schemaCacheManager);
        _privacyTransformer = new PrivacyTransformer(parserService);
        _parserService = parserService;

        // Load initial AI settings from config
        _aiSettings = LoadAiSettings();
    }

    /// <summary>
    /// Reloads <see cref="AiSettings"/> from <see cref="ConfigManager.Load()"/>.
    /// Called when the shell notifies the engine of a settings change.
    /// </summary>
    public void RefreshSettings()
    {
        _aiSettings = LoadAiSettings();
        Log.Information("AI settings refreshed: enabled={Enabled}, provider={Provider}, model={Model}",
            _aiSettings.Enabled, _aiSettings.Provider, _aiSettings.Model);
    }

    // ───────────────────── T060: Privacy Consent Check ─────────────────────

    /// <summary>
    /// Checks whether the user has given privacy consent for cloud AI providers.
    /// Local providers (ollama, lmstudio) do not require consent.
    /// Throws <see cref="PrivacyConsentRequiredException"/> when consent is needed.
    /// </summary>
    /// <param name="settings">Current AI settings.</param>
    private static void CheckPrivacyConsent(AiSettings settings)
    {
        if (!settings.PrivacyConsentRequired)
            return; // Consent already given

        var provider = settings.Provider?.Trim() ?? string.Empty;
        if (LocalProviders.Contains(provider))
            return; // Local providers don't need consent

        // Cloud provider requires consent
        var providerDisplay = string.IsNullOrEmpty(provider) ? "your AI provider" : provider;
        throw new PrivacyConsentRequiredException(
            $"CONSENT_REQUIRED:Data will be sent to {providerDisplay}. Please confirm in settings.");
    }

    // ───────────────────── T061: Retry with Exponential Backoff ─────────────────────

    /// <summary>
    /// Executes an async action with retry logic and exponential backoff for rate-limited (HTTP 429) responses.
    /// </summary>
    /// <typeparam name="T">Return type of the action.</typeparam>
    /// <param name="action">The async action to execute.</param>
    /// <param name="maxRetries">Maximum number of retry attempts.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the action.</returns>
    private static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action, int maxRetries, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (
                attempt < maxRetries &&
                ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                attempt++;
                var delayMs = Math.Min((int)Math.Pow(2, attempt) * 1000, MaxBackoffMs);
                Log.Warning("AI request rate-limited (429), retry {Attempt}/{MaxRetries} after {DelayMs}ms",
                    attempt, maxRetries, delayMs);
                await Task.Delay(delayMs, ct);
            }
        }
    }

    // ───────────────────── T062: Offline Fallback ─────────────────────

    /// <summary>
    /// Calls the AI provider, retrying on rate limits (T061) and falling back to the
    /// offline provider (T062) on transient failures. Returns the response and a flag
    /// indicating whether the fallback was used.
    /// </summary>
    private async Task<(ChatResponse Response, bool UsedFallback)> ExecuteWithFallbackAsync(
        AiSettings settings,
        List<ChatMessage> messages,
        ChatOptions options,
        CancellationToken ct)
    {
        try
        {
            using var primaryClient = AiProviderFactory.Create(settings);
            var response = await ExecuteWithRetryAsync(
                () => primaryClient.GetResponseAsync(messages, options, ct),
                settings.Retries, ct);
            return (response, false);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException &&
            ex is not PrivacyConsentRequiredException &&
            !string.IsNullOrWhiteSpace(settings.OfflineProvider))
        {
            Log.Warning(ex, "Primary AI provider failed, switching to offline fallback provider={FallbackProvider}",
                settings.OfflineProvider);

            try
            {
                using var fallbackClient = AiProviderFactory.CreateFromFallback(settings);
                var response = await fallbackClient.GetResponseAsync(messages, options, ct);
                return (response, true);
            }
            catch (Exception fallbackEx)
            {
                Log.Error(fallbackEx, "Offline fallback provider also failed");
                // Throw the original exception as it's more relevant to the user
                throw new AggregateException(
                    $"Primary provider failed: {ex.Message}. Fallback also failed: {fallbackEx.Message}",
                    ex, fallbackEx);
            }
        }
    }

    // ───────────────────── T063: AI-Generated SQL Validation ─────────────────────

    /// <summary>
    /// Validates AI-generated SQL by parsing it and checking all table/view references
    /// against the provided schema context. Returns annotations for non-existent objects.
    /// </summary>
    /// <param name="sql">The AI-generated SQL to validate.</param>
    /// <param name="context">The schema context containing known database objects.</param>
    /// <returns>List of annotations for any objects not found in the current schema.</returns>
    private List<AnnotationDto> ValidateGeneratedSql(string sql, SchemaContext context)
    {
        var annotations = new List<AnnotationDto>();

        if (string.IsNullOrWhiteSpace(sql) || context.Objects.Count == 0)
            return annotations;

        try
        {
            var script = _parserService.Parse(sql, out _);
            if (script == null)
                return annotations;

            // Build a set of known object names (schema.name, case-insensitive)
            var knownObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in context.Objects)
            {
                // Add both "schema.name" and just "name" for dbo schema default matching
                knownObjects.Add($"{obj.Schema}.{obj.Name}");
                if (obj.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                {
                    knownObjects.Add(obj.Name);
                }
            }

            // Walk the AST and collect all NamedTableReference nodes
            var visitor = new TableReferenceVisitor();
            script.Accept(visitor);

            foreach (var tableRef in visitor.TableReferences)
            {
                var objectName = tableRef.SchemaObject;
                if (objectName == null)
                    continue;

                // Build the fully qualified name
                var schemaName = objectName.SchemaIdentifier?.Value ?? "dbo";
                var tableName = objectName.BaseIdentifier?.Value;
                if (string.IsNullOrEmpty(tableName))
                    continue;

                var qualifiedName = $"{schemaName}.{tableName}";

                // Skip temp tables and table variables
                if (tableName.StartsWith("#") || tableName.StartsWith("@"))
                    continue;

                // Check against known objects
                if (!knownObjects.Contains(qualifiedName) && !knownObjects.Contains(tableName))
                {
                    var line = tableRef.StartLine;
                    annotations.Add(new AnnotationDto
                    {
                        StartLine = line,
                        EndLine = line,
                        Category = "review",
                        Description = $"Object '{qualifiedName}' not found in current schema"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ValidateGeneratedSql: validation failed, returning empty annotations");
        }

        return annotations;
    }

    /// <summary>
    /// AST visitor that collects all <see cref="NamedTableReference"/> nodes from a parsed T-SQL script.
    /// </summary>
    private sealed class TableReferenceVisitor : TSqlFragmentVisitor
    {
        public List<NamedTableReference> TableReferences { get; } = new();

        public override void Visit(NamedTableReference node)
        {
            TableReferences.Add(node);
            base.Visit(node);
        }
    }

    /// <summary>
    /// Exception thrown when privacy consent is required but has not been given.
    /// </summary>
    private sealed class PrivacyConsentRequiredException : Exception
    {
        public PrivacyConsentRequiredException(string message) : base(message) { }
    }

    // ───────────────────── Handler Methods ─────────────────────

    /// <summary>
    /// Handles AiTextToSql (MessageType 70). Generates SQL from a natural-language prompt.
    /// Pipeline: deserialize request -> build schema context -> format schema -> apply privacy
    /// -> build prompt -> call AI provider -> de-transform -> validate SQL -> return response.
    /// </summary>
    public async Task<RpcMessage?> HandleTextToSqlAsync(
        RpcMessage message,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (message.Payload == null)
                return CreateResponse(MessageTypes.AiTextToSqlResult,
                    new AiTextToSqlResponse { Success = false, ErrorMessage = "Payload required" },
                    message.RequestId);

            var request = MessagePackSerializer.Deserialize<AiTextToSqlRequest>(message.Payload);
            Log.Debug("AiTextToSql: session={Session}, prompt length={Length}",
                request.SessionId, request.Prompt.Length);

            var settings = _aiSettings;
            if (!settings.Enabled)
            {
                return CreateResponse(MessageTypes.AiTextToSqlResult,
                    new AiTextToSqlResponse { Success = false, ErrorMessage = "AI assistance is disabled" },
                    message.RequestId);
            }

            // T060: Check privacy consent for cloud providers
            CheckPrivacyConsent(settings);

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return CreateResponse(MessageTypes.AiTextToSqlResult,
                    new AiTextToSqlResponse { Success = false, ErrorMessage = "Prompt cannot be empty" },
                    message.RequestId);
            }

            // Step 1: Build schema context
            var schemaContext = await _schemaContextBuilder.BuildAsync(
                request.SessionId, sessionLookup, request.Prompt, compressionLevel: 2);

            // Step 2: Format schema context for prompt inclusion
            var schemaText = SchemaContextFormatter.Format(schemaContext);

            // Step 3: Apply privacy transformation
            // For text-to-SQL the "SQL" parameter is the natural-language prompt.
            // In anonymous mode the schema context identifiers are hashed; literal redaction
            // only applies to SQL syntax, so the prompt itself passes through mostly unchanged.
            var (transformedPrompt, transformedContext, transformation) =
                _privacyTransformer.Transform(request.Prompt, schemaContext, settings.PrivacyMode);

            // Re-format schema if it was transformed (anonymous mode hashes identifiers)
            if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            {
                schemaText = SchemaContextFormatter.Format(transformedContext);
            }

            // Step 4: Build prompt messages
            var (systemPrompt, userPrompt) = TextToSqlPrompt.Build(schemaText, transformedPrompt);

            // Step 5: Create AI provider client and send request (T061/T062: retry + fallback)
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var options = new ChatOptions
            {
                MaxOutputTokens = settings.MaxTokens,
                Temperature = (float)settings.Temperature
            };

            var (aiResponse, usedFallback) = await ExecuteWithFallbackAsync(
                settings, chatMessages, options, ct);
            var generatedSql = aiResponse.Text ?? string.Empty;

            // Step 6: De-transform if privacy was applied (restore original identifiers)
            if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            {
                generatedSql = PrivacyTransformer.DeTransform(generatedSql, transformation);
            }

            // Step 7: Strip markdown code fences if the model included them despite instructions
            generatedSql = StripCodeFences(generatedSql);

            // Step 8: Validate generated SQL syntax (non-blocking — still return even if invalid)
            _parserService.Parse(generatedSql, out var parseErrors);
            if (parseErrors.Count > 0)
            {
                Log.Debug("AiTextToSql: generated SQL has {ErrorCount} parse error(s), returning anyway",
                    parseErrors.Count);
            }

            // T063: Validate generated SQL against schema (check for non-existent objects)
            var validationAnnotations = ValidateGeneratedSql(generatedSql, schemaContext);

            // Calculate token usage from response metadata
            var tokensUsed = 0;
            if (aiResponse.Usage != null)
            {
                tokensUsed = (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0));
            }

            sw.Stop();

            // T062: Append fallback notice if applicable
            string? fallbackNotice = usedFallback
                ? $"[Note: Response generated by fallback model ({settings.OfflineProvider}/{settings.OfflineModel})]"
                : null;

            Log.Information("AiTextToSql: success, tokens={Tokens}, latency={LatencyMs}ms, parseErrors={ParseErrors}, fallback={Fallback}, validationIssues={ValidationCount}",
                tokensUsed, sw.ElapsedMilliseconds, parseErrors.Count, usedFallback, validationAnnotations.Count);

            return CreateResponse(MessageTypes.AiTextToSqlResult,
                new AiTextToSqlResponse
                {
                    Success = true,
                    GeneratedSql = fallbackNotice != null
                        ? $"-- {fallbackNotice}\n{generatedSql}"
                        : generatedSql,
                    TokensUsed = tokensUsed,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    Annotations = validationAnnotations.Count > 0 ? validationAnnotations : null
                },
                message.RequestId);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            Log.Information("AiTextToSql: privacy consent required");
            return CreateResponse(MessageTypes.AiTextToSqlResult,
                new AiTextToSqlResponse
                {
                    Success = false,
                    ErrorMessage = consentEx.Message,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Debug("AiTextToSql: cancelled after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiTextToSqlResult,
                new AiTextToSqlResponse
                {
                    Success = false,
                    ErrorMessage = "Request was cancelled",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "AiTextToSql handler failed after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiTextToSqlResult,
                new AiTextToSqlResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
    }

    /// <summary>
    /// Strips markdown code fences (```sql ... ```) from AI responses.
    /// Some models include fences despite being instructed not to.
    /// </summary>
    private static string StripCodeFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var trimmed = text.Trim();

        // Check for ```sql or ``` prefix
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            // Remove opening fence (```sql, ```tsql, ```)
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            // Remove closing fence
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
            {
                trimmed = trimmed[..^3];
            }

            return trimmed.Trim();
        }

        return text;
    }

    /// <summary>
    /// Handles AiExplain (MessageType 71). Explains a SQL statement in natural language.
    /// Pipeline: deserialize -> build schema context (level 2) -> format -> apply privacy
    /// -> build prompt -> call AI provider -> parse sections -> return structured response.
    /// </summary>
    public async Task<RpcMessage?> HandleExplainAsync(
        RpcMessage message,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (message.Payload == null)
                return CreateResponse(MessageTypes.AiExplainResult,
                    new AiExplainResponse { Success = false, ErrorMessage = "Payload required" },
                    message.RequestId);

            var request = MessagePackSerializer.Deserialize<AiExplainRequest>(message.Payload);
            Log.Debug("AiExplain: session={Session}, sql length={Length}",
                request.SessionId, request.SelectedSql.Length);

            var settings = _aiSettings;
            if (!settings.Enabled)
            {
                return CreateResponse(MessageTypes.AiExplainResult,
                    new AiExplainResponse { Success = false, ErrorMessage = "AI assistance is disabled" },
                    message.RequestId);
            }

            // T060: Check privacy consent for cloud providers
            CheckPrivacyConsent(settings);

            if (string.IsNullOrWhiteSpace(request.SelectedSql))
            {
                return CreateResponse(MessageTypes.AiExplainResult,
                    new AiExplainResponse { Success = false, ErrorMessage = "No SQL text provided" },
                    message.RequestId);
            }

            // Step 1: Build schema context (level 2 — column details)
            var schemaContext = await _schemaContextBuilder.BuildAsync(
                request.SessionId, sessionLookup, request.SelectedSql, compressionLevel: 2);

            // Step 2: Format schema context
            var schemaText = SchemaContextFormatter.Format(schemaContext);

            // Step 3: Apply privacy transformation
            var (transformedSql, transformedContext, transformation) =
                _privacyTransformer.Transform(request.SelectedSql, schemaContext, settings.PrivacyMode);

            if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            {
                schemaText = SchemaContextFormatter.Format(transformedContext);
            }

            // Step 4: Build prompt
            var (systemPrompt, userPrompt) = ExplainPrompt.Build(schemaText, transformedSql);

            // Step 5: Send to AI provider (T061/T062: retry + fallback)
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var options = new ChatOptions
            {
                MaxOutputTokens = settings.MaxTokens,
                Temperature = (float)settings.Temperature
            };

            var (aiResponse, usedFallback) = await ExecuteWithFallbackAsync(
                settings, chatMessages, options, ct);
            var responseText = aiResponse.Text ?? string.Empty;

            // Step 6: De-transform if privacy was applied
            if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            {
                responseText = PrivacyTransformer.DeTransform(responseText, transformation);
            }

            // Step 7: Parse the response into structured sections
            var (purpose, stepByStep, keyDetails, suggestions) = ParseExplainSections(responseText);

            // T062: Prepend fallback notice
            if (usedFallback)
            {
                purpose = $"[Fallback model: {settings.OfflineProvider}/{settings.OfflineModel}] {purpose}";
            }

            var tokensUsed = 0;
            if (aiResponse.Usage != null)
            {
                tokensUsed = (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0));
            }

            sw.Stop();

            Log.Information("AiExplain: success, tokens={Tokens}, latency={LatencyMs}ms, fallback={Fallback}",
                tokensUsed, sw.ElapsedMilliseconds, usedFallback);

            return CreateResponse(MessageTypes.AiExplainResult,
                new AiExplainResponse
                {
                    Success = true,
                    Purpose = purpose,
                    StepByStep = stepByStep,
                    KeyDetails = keyDetails,
                    Suggestions = suggestions,
                    TokensUsed = tokensUsed,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            return CreateResponse(MessageTypes.AiExplainResult,
                new AiExplainResponse { Success = false, ErrorMessage = consentEx.Message, LatencyMs = (int)sw.ElapsedMilliseconds },
                message.RequestId);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Debug("AiExplain: cancelled after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiExplainResult,
                new AiExplainResponse
                {
                    Success = false,
                    ErrorMessage = "Request was cancelled",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "AiExplain handler failed after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiExplainResult,
                new AiExplainResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
    }

    /// <summary>
    /// Parses an AI explain response into its four structured sections by splitting
    /// on the section headers: PURPOSE:, STEP BY STEP:, KEY DETAILS:, SUGGESTIONS:.
    /// </summary>
    private static (string purpose, string stepByStep, string keyDetails, string suggestions)
        ParseExplainSections(string responseText)
    {
        var purpose = string.Empty;
        var stepByStep = string.Empty;
        var keyDetails = string.Empty;
        var suggestions = string.Empty;

        if (string.IsNullOrWhiteSpace(responseText))
            return (purpose, stepByStep, keyDetails, suggestions);

        // Define section headers in order
        var headers = new[]
        {
            "PURPOSE:",
            "STEP BY STEP:",
            "KEY DETAILS:",
            "SUGGESTIONS:"
        };

        // Find positions of each header (case-insensitive)
        var positions = new List<(int index, string header)>();
        foreach (var header in headers)
        {
            var idx = responseText.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                positions.Add((idx, header));
            }
        }

        // Sort by position
        positions.Sort((a, b) => a.index.CompareTo(b.index));

        // Extract content between headers
        for (int i = 0; i < positions.Count; i++)
        {
            var start = positions[i].index + positions[i].header.Length;
            var end = i + 1 < positions.Count ? positions[i + 1].index : responseText.Length;
            var content = responseText[start..end].Trim();

            switch (positions[i].header.ToUpperInvariant())
            {
                case "PURPOSE:":
                    purpose = content;
                    break;
                case "STEP BY STEP:":
                    stepByStep = content;
                    break;
                case "KEY DETAILS:":
                    keyDetails = content;
                    break;
                case "SUGGESTIONS:":
                    suggestions = content;
                    break;
            }
        }

        // Fallback: if no headers were found, put everything in purpose
        if (positions.Count == 0)
        {
            purpose = responseText.Trim();
        }

        return (purpose, stepByStep, keyDetails, suggestions);
    }

    /// <summary>
    /// Handles AiFix (MessageType 72). Suggests a fix for a failing SQL statement.
    /// Pipeline: deserialize -> build schema context (level 3) -> apply privacy (redact literals)
    /// -> build prompt -> call AI provider -> parse sections -> build annotations -> de-transform -> return.
    /// </summary>
    public async Task<RpcMessage?> HandleFixAsync(
        RpcMessage message,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (message.Payload == null)
                return CreateResponse(MessageTypes.AiFixResult,
                    new AiFixResponse { Success = false, ErrorMessage = "Payload required" },
                    message.RequestId);

            var request = MessagePackSerializer.Deserialize<AiFixRequest>(message.Payload);
            Log.Debug("AiFix: session={Session}, error={Error}, sql length={Length}",
                request.SessionId, request.ErrorMessage, request.FailingSql.Length);

            var settings = _aiSettings;
            if (!settings.Enabled)
                return CreateResponse(MessageTypes.AiFixResult,
                    new AiFixResponse { Success = false, ErrorMessage = "AI assistance is disabled" },
                    message.RequestId);

            // T060: Check privacy consent for cloud providers
            CheckPrivacyConsent(settings);

            if (string.IsNullOrWhiteSpace(request.FailingSql))
                return CreateResponse(MessageTypes.AiFixResult,
                    new AiFixResponse { Success = false, ErrorMessage = "No failing SQL provided" },
                    message.RequestId);

            // Build schema context (level 3 for column/FK/index info)
            var schemaContext = await _schemaContextBuilder.BuildAsync(
                request.SessionId, sessionLookup, request.FailingSql, compressionLevel: 3);
            var schemaText = SchemaContextFormatter.Format(schemaContext);

            // Apply privacy transformation
            var (transformedSql, transformedContext, transformation) =
                _privacyTransformer.Transform(request.FailingSql, schemaContext, settings.PrivacyMode);
            if (transformedContext != null && transformation.IdentifierMap.Count > 0)
                schemaText = SchemaContextFormatter.Format(transformedContext);

            // Build prompt and send to AI provider (T061/T062: retry + fallback)
            var (systemPrompt, userPrompt) = FixPrompt.Build(
                schemaText, transformedSql, request.ErrorMessage, request.ErrorNumber);

            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };
            var options = new ChatOptions
            {
                MaxOutputTokens = settings.MaxTokens,
                Temperature = (float)settings.Temperature
            };

            var (aiResponse, usedFallback) = await ExecuteWithFallbackAsync(
                settings, chatMessages, options, ct);
            var responseText = aiResponse.Text ?? string.Empty;

            // Parse FIXED SQL and EXPLANATION sections
            var fixedSql = ExtractSection(responseText, "FIXED SQL:");
            var explanation = ExtractSection(responseText, "EXPLANATION:");
            if (string.IsNullOrEmpty(fixedSql))
                (fixedSql, explanation) = ParseFixSectionsFallback(responseText);

            // De-transform and strip code fences
            if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            {
                fixedSql = PrivacyTransformer.DeTransform(fixedSql, transformation);
                explanation = PrivacyTransformer.DeTransform(explanation, transformation);
            }
            fixedSql = StripCodeFences(fixedSql);

            // Build line-level annotations (diff-based)
            var fixAnnotations = BuildDiffAnnotations(request.FailingSql, fixedSql);

            // T063: Validate generated SQL against schema
            var validationAnnotations = ValidateGeneratedSql(fixedSql, schemaContext);
            if (validationAnnotations.Count > 0)
                fixAnnotations.AddRange(validationAnnotations);

            // T062: Prepend fallback notice to explanation
            if (usedFallback)
            {
                explanation = $"[Note: Response generated by fallback model ({settings.OfflineProvider}/{settings.OfflineModel})]\n{explanation}";
            }

            var tokensUsed = 0;
            if (aiResponse.Usage != null)
                tokensUsed = (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0));

            sw.Stop();
            Log.Information("AiFix: success, tokens={Tokens}, latency={LatencyMs}ms, annotations={AnnotationCount}, fallback={Fallback}",
                tokensUsed, sw.ElapsedMilliseconds, fixAnnotations.Count, usedFallback);

            return CreateResponse(MessageTypes.AiFixResult,
                new AiFixResponse
                {
                    Success = true,
                    FixedSql = fixedSql,
                    Explanation = explanation,
                    Annotations = fixAnnotations,
                    TokensUsed = tokensUsed,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            return CreateResponse(MessageTypes.AiFixResult,
                new AiFixResponse { Success = false, ErrorMessage = consentEx.Message, LatencyMs = (int)sw.ElapsedMilliseconds },
                message.RequestId);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return CreateResponse(MessageTypes.AiFixResult,
                new AiFixResponse { Success = false, ErrorMessage = "Request was cancelled", LatencyMs = (int)sw.ElapsedMilliseconds },
                message.RequestId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "AiFix handler failed after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiFixResult,
                new AiFixResponse { Success = false, ErrorMessage = ex.Message, LatencyMs = (int)sw.ElapsedMilliseconds },
                message.RequestId);
        }
    }

    /// <summary>
    /// Fallback parser for AI fix responses that do not use the expected section headers.
    /// </summary>
    private static (string fixedSql, string explanation) ParseFixSectionsFallback(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return (string.Empty, string.Empty);

        var explanationIdx = responseText.IndexOf("EXPLANATION:", StringComparison.OrdinalIgnoreCase);
        if (explanationIdx >= 0)
            return (responseText[..explanationIdx].Trim(), responseText[(explanationIdx + "EXPLANATION:".Length)..].Trim());

        return (responseText.Trim(), string.Empty);
    }

    /// <summary>
    /// Builds line-level <see cref="AnnotationDto"/> entries by comparing original SQL with fixed SQL.
    /// Unchanged lines are skipped; whitespace-only changes are "safe"; structural changes are "review".
    /// </summary>
    private static List<AnnotationDto> BuildDiffAnnotations(string originalSql, string fixedSql)
    {
        var result = new List<AnnotationDto>();
        if (string.IsNullOrEmpty(originalSql) || string.IsNullOrEmpty(fixedSql))
            return result;

        var originalLines = originalSql.Split('\n');
        var fixedLines = fixedSql.Split('\n');
        var maxLines = Math.Max(originalLines.Length, fixedLines.Length);

        for (int i = 0; i < maxLines; i++)
        {
            var origLine = i < originalLines.Length ? originalLines[i].TrimEnd('\r') : string.Empty;
            var fixLine = i < fixedLines.Length ? fixedLines[i].TrimEnd('\r') : string.Empty;
            if (string.Equals(origLine, fixLine, StringComparison.Ordinal))
                continue;

            var lineNumber = i + 1;
            var origTrimmed = origLine.Trim();
            var fixTrimmed = fixLine.Trim();

            string category;
            string description;

            if (string.Equals(origTrimmed, fixTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                category = "safe";
                description = "Whitespace or casing change";
            }
            else if (string.IsNullOrEmpty(origTrimmed) && !string.IsNullOrEmpty(fixTrimmed))
            {
                category = "review";
                description = "New line added";
            }
            else if (!string.IsNullOrEmpty(origTrimmed) && string.IsNullOrEmpty(fixTrimmed))
            {
                category = "review";
                description = "Line removed";
            }
            else
            {
                category = "review";
                description = "Structural change";
            }

            result.Add(new AnnotationDto
            {
                StartLine = lineNumber,
                EndLine = lineNumber,
                Category = category,
                Description = description
            });
        }

        return result;
    }

    /// <summary>
    /// Handles AiOptimize (MessageType 73). Suggests performance optimizations for a SQL statement.
    /// Pipeline: deserialize -> build schema context (level 3, includes indexes) -> format
    /// -> apply privacy -> build prompt via OptimizePrompt -> call AI provider -> parse response
    /// sections (optimized SQL, explanation, annotations, index suggestions) -> de-transform -> return.
    /// </summary>
    public async Task<RpcMessage?> HandleOptimizeAsync(
        RpcMessage message,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (message.Payload == null)
                return CreateResponse(MessageTypes.AiOptimizeResult,
                    new AiOptimizeResponse { Success = false, ErrorMessage = "Payload required" },
                    message.RequestId);

            var request = MessagePackSerializer.Deserialize<AiOptimizeRequest>(message.Payload);
            Log.Debug("AiOptimize: session={Session}, sql length={Length}",
                request.SessionId, request.SelectedSql.Length);

            var settings = _aiSettings;
            if (!settings.Enabled)
            {
                return CreateResponse(MessageTypes.AiOptimizeResult,
                    new AiOptimizeResponse { Success = false, ErrorMessage = "AI assistance is disabled" },
                    message.RequestId);
            }

            // T060: Check privacy consent for cloud providers
            CheckPrivacyConsent(settings);

            if (string.IsNullOrWhiteSpace(request.SelectedSql))
            {
                return CreateResponse(MessageTypes.AiOptimizeResult,
                    new AiOptimizeResponse { Success = false, ErrorMessage = "No SQL text provided" },
                    message.RequestId);
            }

            // Step 1: Build schema context at level 3 (includes indexes for optimization analysis)
            var schemaContext = await _schemaContextBuilder.BuildAsync(
                request.SessionId, sessionLookup, request.SelectedSql, compressionLevel: 3);

            // Step 2: Format schema context
            var schemaText = SchemaContextFormatter.Format(schemaContext);

            // Step 3: Apply privacy transformation
            var (transformedSql, transformedContext, transformation) =
                _privacyTransformer.Transform(request.SelectedSql, schemaContext, settings.PrivacyMode);

            if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            {
                schemaText = SchemaContextFormatter.Format(transformedContext);
            }

            // Step 4: Build prompt via OptimizePrompt
            var (systemPrompt, userPrompt) = OptimizePrompt.Build(schemaText, transformedSql);

            // Step 5: Send to AI provider (T061/T062: retry + fallback)
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var options = new ChatOptions
            {
                MaxOutputTokens = settings.MaxTokens,
                Temperature = (float)settings.Temperature
            };

            var (aiResponse, usedFallback) = await ExecuteWithFallbackAsync(
                settings, chatMessages, options, ct);
            var responseText = aiResponse.Text ?? string.Empty;

            // Step 6: De-transform if privacy was applied
            if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            {
                responseText = PrivacyTransformer.DeTransform(responseText, transformation);
            }

            // Step 7: Parse the response into structured sections
            var optimizedSql = ExtractSection(responseText, "OPTIMIZED SQL:");
            var explanation = ExtractSection(responseText, "EXPLANATION:");
            var annotationsText = ExtractSection(responseText, "ANNOTATIONS:");
            var indexSuggestionsText = ExtractSection(responseText, "INDEX SUGGESTIONS:");

            // Strip code fences from the optimized SQL
            optimizedSql = StripCodeFences(optimizedSql);

            // Parse annotations
            var annotations = ParseAnnotations(annotationsText);

            // T063: Validate generated SQL against schema
            var validationAnnotations = ValidateGeneratedSql(optimizedSql, schemaContext);
            if (validationAnnotations.Count > 0)
                annotations.AddRange(validationAnnotations);

            // Parse index suggestions
            var indexSuggestions = ParseIndexSuggestions(indexSuggestionsText);

            // T062: Prepend fallback notice to explanation
            if (usedFallback)
            {
                explanation = $"[Note: Response generated by fallback model ({settings.OfflineProvider}/{settings.OfflineModel})]\n{explanation}";
            }

            var tokensUsed = 0;
            if (aiResponse.Usage != null)
            {
                tokensUsed = (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0));
            }

            sw.Stop();

            Log.Information("AiOptimize: success, tokens={Tokens}, latency={LatencyMs}ms, annotations={AnnotationCount}, indexSuggestions={IndexCount}, fallback={Fallback}",
                tokensUsed, sw.ElapsedMilliseconds, annotations.Count, indexSuggestions.Count, usedFallback);

            return CreateResponse(MessageTypes.AiOptimizeResult,
                new AiOptimizeResponse
                {
                    Success = true,
                    OptimizedSql = optimizedSql,
                    Explanation = explanation,
                    Annotations = annotations,
                    IndexSuggestions = indexSuggestions,
                    TokensUsed = tokensUsed,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            return CreateResponse(MessageTypes.AiOptimizeResult,
                new AiOptimizeResponse { Success = false, ErrorMessage = consentEx.Message, LatencyMs = (int)sw.ElapsedMilliseconds },
                message.RequestId);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Debug("AiOptimize: cancelled after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiOptimizeResult,
                new AiOptimizeResponse
                {
                    Success = false,
                    ErrorMessage = "Request was cancelled",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "AiOptimize handler failed after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiOptimizeResult,
                new AiOptimizeResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
    }

    /// <summary>
    /// Handles AiIndexAnalysis (MessageType 74). Analyzes a SQL statement for index improvement suggestions.
    /// Pipeline: deserialize -> build schema context (level 3, with existing indexes) -> format
    /// -> apply privacy -> build prompt via IndexAnalysisPrompt (include execution plan XML if available)
    /// -> call AI provider -> parse CREATE INDEX scripts and metadata -> return structured response.
    /// </summary>
    public async Task<RpcMessage?> HandleIndexAnalysisAsync(
        RpcMessage message,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (message.Payload == null)
                return CreateResponse(MessageTypes.AiIndexAnalysisResult,
                    new AiIndexAnalysisResponse { Success = false, ErrorMessage = "Payload required" },
                    message.RequestId);

            var request = MessagePackSerializer.Deserialize<AiIndexAnalysisRequest>(message.Payload);
            Log.Debug("AiIndexAnalysis: session={Session}, sql length={Length}, hasPlan={HasPlan}",
                request.SessionId, request.SelectedSql.Length, request.ExecutionPlanXml != null);

            var settings = _aiSettings;
            if (!settings.Enabled)
            {
                return CreateResponse(MessageTypes.AiIndexAnalysisResult,
                    new AiIndexAnalysisResponse { Success = false, ErrorMessage = "AI assistance is disabled" },
                    message.RequestId);
            }

            // T060: Check privacy consent for cloud providers
            CheckPrivacyConsent(settings);

            if (string.IsNullOrWhiteSpace(request.SelectedSql))
            {
                return CreateResponse(MessageTypes.AiIndexAnalysisResult,
                    new AiIndexAnalysisResponse { Success = false, ErrorMessage = "No SQL text provided" },
                    message.RequestId);
            }

            // Step 1: Build schema context at level 3 (with existing indexes)
            var schemaContext = await _schemaContextBuilder.BuildAsync(
                request.SessionId, sessionLookup, request.SelectedSql, compressionLevel: 3);

            // Step 2: Format schema context
            var schemaText = SchemaContextFormatter.Format(schemaContext);

            // Step 3: Apply privacy transformation
            var (transformedSql, transformedContext, transformation) =
                _privacyTransformer.Transform(request.SelectedSql, schemaContext, settings.PrivacyMode);

            if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            {
                schemaText = SchemaContextFormatter.Format(transformedContext);
            }

            // Transform execution plan XML if provided (de-transform will reverse it)
            string? transformedPlanXml = null;
            if (!string.IsNullOrWhiteSpace(request.ExecutionPlanXml))
            {
                if (transformation.IdentifierMap.Count > 0)
                {
                    // In anonymous mode, also transform identifiers in the execution plan
                    var planText = request.ExecutionPlanXml;
                    foreach (var (original, hashed) in transformation.IdentifierMap
                                 .Select(kvp => (kvp.Value, kvp.Key)))
                    {
                        planText = planText.Replace(original, hashed, StringComparison.OrdinalIgnoreCase);
                    }
                    transformedPlanXml = planText;
                }
                else
                {
                    transformedPlanXml = request.ExecutionPlanXml;
                }
            }

            // Step 4: Build prompt via IndexAnalysisPrompt
            var (systemPrompt, userPrompt) = IndexAnalysisPrompt.Build(
                schemaText, transformedSql, transformedPlanXml);

            // Step 5: Send to AI provider (T061/T062: retry + fallback)
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var options = new ChatOptions
            {
                MaxOutputTokens = settings.MaxTokens,
                Temperature = (float)settings.Temperature
            };

            var (aiResponse, usedFallback) = await ExecuteWithFallbackAsync(
                settings, chatMessages, options, ct);
            var responseText = aiResponse.Text ?? string.Empty;

            // Step 6: De-transform if privacy was applied
            if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            {
                responseText = PrivacyTransformer.DeTransform(responseText, transformation);
            }

            // Step 7: Parse the response into structured sections
            var summary = ExtractSection(responseText, "SUMMARY:");
            var indexSuggestionsText = ExtractSection(responseText, "INDEX SUGGESTIONS:");

            // Parse index suggestions from the response text
            var suggestions = ParseIndexSuggestions(indexSuggestionsText);

            // T062: Prepend fallback notice to summary
            if (usedFallback)
            {
                summary = $"[Note: Response generated by fallback model ({settings.OfflineProvider}/{settings.OfflineModel})]\n{summary}";
            }

            var tokensUsed = 0;
            if (aiResponse.Usage != null)
            {
                tokensUsed = (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0));
            }

            sw.Stop();

            Log.Information("AiIndexAnalysis: success, tokens={Tokens}, latency={LatencyMs}ms, suggestions={SuggestionCount}, fallback={Fallback}",
                tokensUsed, sw.ElapsedMilliseconds, suggestions.Count, usedFallback);

            return CreateResponse(MessageTypes.AiIndexAnalysisResult,
                new AiIndexAnalysisResponse
                {
                    Success = true,
                    Suggestions = suggestions,
                    Summary = summary,
                    TokensUsed = tokensUsed,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            return CreateResponse(MessageTypes.AiIndexAnalysisResult,
                new AiIndexAnalysisResponse { Success = false, ErrorMessage = consentEx.Message, LatencyMs = (int)sw.ElapsedMilliseconds },
                message.RequestId);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Debug("AiIndexAnalysis: cancelled after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiIndexAnalysisResult,
                new AiIndexAnalysisResponse
                {
                    Success = false,
                    ErrorMessage = "Request was cancelled",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "AiIndexAnalysis handler failed after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiIndexAnalysisResult,
                new AiIndexAnalysisResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
    }

    /// <summary>
    /// Handles AiChat (MessageType 75). Processes a user message in the AI chat panel.
    /// Pipeline: deserialize -> build schema context (level 2) -> format -> apply privacy
    /// -> build messages list (system + history + user) -> call AI provider -> extract code blocks
    /// -> return response with code actions.
    /// </summary>
    public async Task<RpcMessage?> HandleChatAsync(
        RpcMessage message,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (message.Payload == null)
                return CreateResponse(MessageTypes.AiChatResult,
                    new AiChatResponse { Success = false, ErrorMessage = "Payload required" },
                    message.RequestId);

            var request = MessagePackSerializer.Deserialize<AiChatRequest>(message.Payload);
            Log.Debug("AiChat: session={Session}, message length={Length}, history={HistoryCount}",
                request.SessionId, request.Message.Length, request.History.Count);

            var settings = _aiSettings;
            if (!settings.Enabled)
            {
                return CreateResponse(MessageTypes.AiChatResult,
                    new AiChatResponse { Success = false, ErrorMessage = "AI assistance is disabled" },
                    message.RequestId);
            }

            // T060: Check privacy consent for cloud providers
            CheckPrivacyConsent(settings);

            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return CreateResponse(MessageTypes.AiChatResult,
                    new AiChatResponse { Success = false, ErrorMessage = "Message cannot be empty" },
                    message.RequestId);
            }

            // Step 1: Build schema context (level 2 — column details for chat context)
            var schemaContext = await _schemaContextBuilder.BuildAsync(
                request.SessionId, sessionLookup, request.Message, compressionLevel: 2);

            // Step 2: Format schema context
            var schemaText = SchemaContextFormatter.Format(schemaContext);

            // Step 3: Apply privacy transformation to user message
            var (transformedMessage, transformedContext, transformation) =
                _privacyTransformer.Transform(request.Message, schemaContext, settings.PrivacyMode);

            if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            {
                schemaText = SchemaContextFormatter.Format(transformedContext);
            }

            // Step 4: Build messages list — system prompt + conversation history + new user message
            var systemPrompt = ChatSystemPrompt.Build(schemaText);

            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt)
            };

            // Map conversation history (ChatTurnDto -> ChatMessage)
            // Apply the same privacy transformation to history turns to prevent leaking
            // unredacted literals/identifiers from prior turns in schemaOnly/anonymous modes.
            if (request.History.Count > 0)
            {
                foreach (var turn in request.History)
                {
                    var role = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                        ? ChatRole.Assistant
                        : ChatRole.User;
                    var content = turn.Content;
                    if (role == ChatRole.User && transformation.IdentifierMap.Count > 0)
                    {
                        // Re-apply the same privacy transformation to prior user messages
                        var (transformedTurn, _, _) =
                            _privacyTransformer.Transform(content, schemaContext, settings.PrivacyMode);
                        content = transformedTurn;
                    }
                    chatMessages.Add(new ChatMessage(role, content));
                }
            }

            // Add the new user message
            chatMessages.Add(new ChatMessage(ChatRole.User, transformedMessage));

            // Step 5: Send to AI provider (T061/T062: retry + fallback)
            var options = new ChatOptions
            {
                MaxOutputTokens = settings.MaxTokens,
                Temperature = (float)settings.Temperature
            };

            var (aiResponse, usedFallback) = await ExecuteWithFallbackAsync(
                settings, chatMessages, options, ct);
            var responseText = aiResponse.Text ?? string.Empty;

            // Step 6: De-transform if privacy was applied
            if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            {
                responseText = PrivacyTransformer.DeTransform(responseText, transformation);
            }

            // T062: Prepend fallback notice to response
            if (usedFallback)
            {
                responseText = $"*[Note: Response generated by fallback model ({settings.OfflineProvider}/{settings.OfflineModel})]*\n\n{responseText}";
            }

            // Step 7: Extract SQL code blocks and create CodeActionDto entries
            var codeActions = ExtractCodeActions(responseText);

            var tokensUsed = 0;
            if (aiResponse.Usage != null)
            {
                tokensUsed = (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0));
            }

            sw.Stop();

            Log.Information("AiChat: success, tokens={Tokens}, latency={LatencyMs}ms, codeActions={Actions}, fallback={Fallback}",
                tokensUsed, sw.ElapsedMilliseconds, codeActions.Count, usedFallback);

            return CreateResponse(MessageTypes.AiChatResult,
                new AiChatResponse
                {
                    Success = true,
                    Response = responseText,
                    CodeActions = codeActions,
                    TokensUsed = tokensUsed,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            return CreateResponse(MessageTypes.AiChatResult,
                new AiChatResponse { Success = false, ErrorMessage = consentEx.Message, LatencyMs = (int)sw.ElapsedMilliseconds },
                message.RequestId);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Debug("AiChat: cancelled after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiChatResult,
                new AiChatResponse
                {
                    Success = false,
                    ErrorMessage = "Request was cancelled",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "AiChat handler failed after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiChatResult,
                new AiChatResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
    }

    /// <summary>
    /// Regex pattern matching ```sql ... ``` code blocks in AI responses.
    /// </summary>
    private static readonly Regex SqlCodeBlockPattern = new(
        @"```sql\s*\n(.*?)```",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Extracts SQL code blocks from an AI response and creates <see cref="CodeActionDto"/> entries
    /// with "Copy Script" label and "copyToClipboard" action type for each.
    /// </summary>
    private static List<CodeActionDto> ExtractCodeActions(string responseText)
    {
        var actions = new List<CodeActionDto>();

        if (string.IsNullOrWhiteSpace(responseText))
            return actions;

        var matches = SqlCodeBlockPattern.Matches(responseText);
        var blockIndex = 0;

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                var sql = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(sql))
                {
                    blockIndex++;
                    actions.Add(new CodeActionDto
                    {
                        Label = blockIndex == 1 ? "Copy Script" : $"Copy Script {blockIndex}",
                        ActionType = "copyToClipboard",
                        Code = sql
                    });
                }
            }
        }

        return actions;
    }

    /// <summary>
    /// Handles AiGhostText (MessageType 76). Generates inline ghost-text completion predictions.
    /// Pipeline: deserialize -> build minimal schema context (level 1-2, budget for &lt;500ms latency)
    /// -> apply privacy -> build prompt -> call AI provider (non-streaming, low MaxOutputTokens)
    /// -> strip fences -> return predicted text.
    /// </summary>
    public async Task<RpcMessage?> HandleGhostTextAsync(
        RpcMessage message,
        Func<string, (string? ConnectionString, string? DatabaseName)> sessionLookup,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            if (message.Payload == null)
                return CreateResponse(MessageTypes.AiGhostTextResult,
                    new AiGhostTextResponse { Success = false, ErrorMessage = "Payload required" },
                    message.RequestId);

            var request = MessagePackSerializer.Deserialize<AiGhostTextRequest>(message.Payload);
            Log.Debug("AiGhostText: session={Session}, cursor={Offset}, preceding length={Length}",
                request.SessionId, request.CursorOffset, request.PrecedingText.Length);

            var settings = _aiSettings;
            if (!settings.Enabled || !settings.InlineCompletion)
            {
                return CreateResponse(MessageTypes.AiGhostTextResult,
                    new AiGhostTextResponse
                    {
                        Success = false,
                        CursorOffset = request.CursorOffset,
                        ErrorMessage = "Inline completion is disabled"
                    },
                    message.RequestId);
            }

            // T060: Check privacy consent for cloud providers
            CheckPrivacyConsent(settings);

            if (string.IsNullOrWhiteSpace(request.PrecedingText))
            {
                return CreateResponse(MessageTypes.AiGhostTextResult,
                    new AiGhostTextResponse
                    {
                        Success = false,
                        CursorOffset = request.CursorOffset,
                        ErrorMessage = "No preceding text provided"
                    },
                    message.RequestId);
            }

            // Step 1: Build minimal schema context (level 1 for speed, limited objects)
            var schemaContext = await _schemaContextBuilder.BuildAsync(
                request.SessionId, sessionLookup, request.PrecedingText,
                compressionLevel: 1, maxObjects: 50);

            var schemaText = SchemaContextFormatter.Format(schemaContext);

            // Check estimated token count — if schema is too large, trim further
            var estimatedTokens = TokenEstimator.EstimateTokensForModel(schemaText, settings.Provider);
            if (estimatedTokens > 2000)
            {
                schemaContext = await _schemaContextBuilder.BuildAsync(
                    request.SessionId, sessionLookup, request.PrecedingText,
                    compressionLevel: 1, maxObjects: 30);
                schemaText = SchemaContextFormatter.Format(schemaContext);
            }

            // Step 2: Apply privacy transformation
            var (transformedPreceding, transformedContext, transformation) =
                _privacyTransformer.Transform(request.PrecedingText, schemaContext, settings.PrivacyMode);

            if (transformedContext != null && transformation.IdentifierMap.Count > 0)
            {
                schemaText = SchemaContextFormatter.Format(transformedContext);
            }

            // Step 3: Build prompt via GhostTextPrompt
            var (systemPrompt, userPrompt) = GhostTextPrompt.Build(schemaText, transformedPreceding);

            // Step 4: Send to AI provider (T061/T062: retry + fallback, non-streaming, low MaxOutputTokens)
            var chatMessages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var options = new ChatOptions
            {
                MaxOutputTokens = 150, // Keep output short for fast response
                Temperature = 0.2f     // Low temperature for deterministic completions
            };

            var (aiResponse, _) = await ExecuteWithFallbackAsync(
                settings, chatMessages, options, ct);
            var predictedText = aiResponse.Text ?? string.Empty;

            // Step 5: De-transform if privacy was applied
            if (transformation.IdentifierMap.Count > 0 || transformation.LiteralMap.Count > 0)
            {
                predictedText = PrivacyTransformer.DeTransform(predictedText, transformation);
            }

            // Step 6: Strip code fences if the model added them
            predictedText = StripCodeFences(predictedText);
            predictedText = predictedText.TrimEnd();

            var tokensUsed = 0;
            if (aiResponse.Usage != null)
            {
                tokensUsed = (int)((aiResponse.Usage.InputTokenCount ?? 0) + (aiResponse.Usage.OutputTokenCount ?? 0));
            }

            sw.Stop();

            Log.Debug("AiGhostText: success, tokens={Tokens}, latency={LatencyMs}ms, prediction length={PredLength}",
                tokensUsed, sw.ElapsedMilliseconds, predictedText.Length);

            return CreateResponse(MessageTypes.AiGhostTextResult,
                new AiGhostTextResponse
                {
                    Success = true,
                    PredictedText = predictedText,
                    CursorOffset = request.CursorOffset,
                    TokensUsed = tokensUsed,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            return CreateResponse(MessageTypes.AiGhostTextResult,
                new AiGhostTextResponse { Success = false, CursorOffset = 0, ErrorMessage = consentEx.Message, LatencyMs = (int)sw.ElapsedMilliseconds },
                message.RequestId);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Debug("AiGhostText: cancelled after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiGhostTextResult,
                new AiGhostTextResponse
                {
                    Success = false,
                    CursorOffset = 0,
                    ErrorMessage = "Request was cancelled",
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "AiGhostText handler failed after {LatencyMs}ms", sw.ElapsedMilliseconds);
            return CreateResponse(MessageTypes.AiGhostTextResult,
                new AiGhostTextResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    LatencyMs = (int)sw.ElapsedMilliseconds
                },
                message.RequestId);
        }
    }

    // ───────────────────── Response-parsing helpers ─────────────────────

    /// <summary>
    /// Extracts the content of a named section from an AI response text.
    /// Looks for a line starting with <paramref name="sectionHeader"/> (case-insensitive)
    /// and returns all text up to the next known section header or end of string.
    /// </summary>
    private static string ExtractSection(string responseText, string sectionHeader)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return string.Empty;

        var startIdx = responseText.IndexOf(sectionHeader, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0)
            return string.Empty;

        var contentStart = startIdx + sectionHeader.Length;

        // Known section headers that could follow
        string[] knownHeaders =
        [
            "OPTIMIZED SQL:", "EXPLANATION:", "ANNOTATIONS:", "INDEX SUGGESTIONS:",
            "SUMMARY:", "PURPOSE:", "STEP BY STEP:", "KEY DETAILS:", "SUGGESTIONS:",
            "FIXED SQL:"
        ];

        var endIdx = responseText.Length;
        foreach (var header in knownHeaders)
        {
            if (string.Equals(header, sectionHeader, StringComparison.OrdinalIgnoreCase))
                continue;

            var headerIdx = responseText.IndexOf(header, contentStart, StringComparison.OrdinalIgnoreCase);
            if (headerIdx >= 0 && headerIdx < endIdx)
            {
                endIdx = headerIdx;
            }
        }

        return responseText[contentStart..endIdx].Trim();
    }

    /// <summary>
    /// Parses annotation lines from the AI response ANNOTATIONS section.
    /// Expected format: <c>[SAFE] Line N: Description</c> or <c>[REVIEW] Line N-M: Description</c>.
    /// </summary>
    private static List<AnnotationDto> ParseAnnotations(string annotationsText)
    {
        var annotations = new List<AnnotationDto>();
        if (string.IsNullOrWhiteSpace(annotationsText))
            return annotations;

        // Pattern: [SAFE|REVIEW] Line N(-M)?: Description
        var regex = new Regex(
            @"\[(SAFE|REVIEW)\]\s*Line\s+(\d+)(?:\s*[-–]\s*(\d+))?\s*:\s*(.+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (var line in annotationsText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = regex.Match(line.Trim());
            if (!match.Success)
                continue;

            var category = match.Groups[1].Value.ToLowerInvariant();
            var startLine = int.TryParse(match.Groups[2].Value, out var sl) ? sl : 0;
            var endLine = match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out var el)
                ? el
                : startLine;
            var description = match.Groups[4].Value.Trim();

            annotations.Add(new AnnotationDto
            {
                StartLine = startLine,
                EndLine = endLine,
                Category = category,
                Description = description
            });
        }

        return annotations;
    }

    /// <summary>
    /// Parses CREATE INDEX statements and their metadata comments from the AI response.
    /// Expected format: <c>CREATE INDEX [IX_Name] ON [schema].[table]([cols]) INCLUDE ([cols]) -- Improvement: X%, Size: YKB, Write Impact: Z</c>.
    /// </summary>
    private static List<IndexSuggestionDto> ParseIndexSuggestions(string indexText)
    {
        var suggestions = new List<IndexSuggestionDto>();
        if (string.IsNullOrWhiteSpace(indexText))
            return suggestions;

        // Check for "None" indicator
        if (indexText.Trim().StartsWith("None", StringComparison.OrdinalIgnoreCase))
            return suggestions;

        // Match CREATE INDEX statements (may span multiple lines if the AI wraps them)
        var createIndexRegex = new Regex(
            @"CREATE\s+(?:NONCLUSTERED\s+)?INDEX\s+\[?([^\]\s]+)\]?\s+ON\s+\[?([^\]\s(]+)\]?\s*\(\s*([^)]+)\s*\)(?:\s*INCLUDE\s*\(\s*([^)]+)\s*\))?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Split on CREATE INDEX to handle multi-line statements
        var lines = indexText.Split('\n');
        var currentStatement = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            // Start of a new CREATE INDEX statement
            if (line.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                // Process previous statement if any
                if (!string.IsNullOrEmpty(currentStatement))
                {
                    TryParseIndexStatement(currentStatement, createIndexRegex, suggestions);
                }
                currentStatement = line;
            }
            else if (!string.IsNullOrEmpty(currentStatement))
            {
                // Continuation of current statement
                currentStatement += " " + line;
            }
        }

        // Process the last statement
        if (!string.IsNullOrEmpty(currentStatement))
        {
            TryParseIndexStatement(currentStatement, createIndexRegex, suggestions);
        }

        return suggestions;
    }

    /// <summary>
    /// Tries to parse a single CREATE INDEX statement string into an <see cref="IndexSuggestionDto"/>.
    /// </summary>
    private static void TryParseIndexStatement(string statement, Regex createIndexRegex, List<IndexSuggestionDto> suggestions)
    {
        var match = createIndexRegex.Match(statement);
        if (!match.Success)
            return;

        var tableName = match.Groups[2].Value.Trim().Trim('[', ']');
        var keyColumnsRaw = match.Groups[3].Value;
        var includeColumnsRaw = match.Groups[4].Success ? match.Groups[4].Value : null;

        var keyColumns = ParseColumnList(keyColumnsRaw);
        var includeColumns = includeColumnsRaw != null ? ParseColumnList(includeColumnsRaw) : null;

        // Extract the full CREATE statement (up to the comment marker)
        var commentIdx = statement.IndexOf("--", StringComparison.Ordinal);
        var createScript = commentIdx >= 0 ? statement[..commentIdx].Trim() : statement.Trim();

        // Parse metadata from trailing comment
        var improvement = string.Empty;
        var estimatedSizeKb = 0;
        var writeImpact = string.Empty;

        if (commentIdx >= 0)
        {
            var comment = statement[(commentIdx + 2)..];

            // Parse Improvement: X%
            var improvementMatch = Regex.Match(comment, @"Improvement:\s*~?(\d+)%", RegexOptions.IgnoreCase);
            if (improvementMatch.Success)
            {
                improvement = $"~{improvementMatch.Groups[1].Value}% faster reads";
            }

            // Parse Size: YKB
            var sizeMatch = Regex.Match(comment, @"Size:\s*~?(\d+)\s*KB", RegexOptions.IgnoreCase);
            if (sizeMatch.Success && int.TryParse(sizeMatch.Groups[1].Value, out var size))
            {
                estimatedSizeKb = size;
            }

            // Parse Write Impact: Low|Medium|High
            var writeMatch = Regex.Match(comment, @"Write\s+Impact:\s*(Low|Medium|High)", RegexOptions.IgnoreCase);
            if (writeMatch.Success)
            {
                writeImpact = writeMatch.Groups[1].Value;
            }
        }

        suggestions.Add(new IndexSuggestionDto
        {
            CreateScript = createScript,
            TableName = tableName,
            Columns = keyColumns,
            IncludeColumns = includeColumns,
            EstimatedImprovement = improvement,
            EstimatedSizeKb = estimatedSizeKb,
            WriteImpact = writeImpact
        });
    }

    /// <summary>
    /// Parses a comma-separated list of column names, stripping brackets and whitespace.
    /// </summary>
    private static List<string> ParseColumnList(string columnsRaw)
    {
        var columns = new List<string>();
        foreach (var part in columnsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var col = part.Trim().Trim('[', ']').Trim();
            if (!string.IsNullOrEmpty(col))
            {
                // Remove ASC/DESC suffix if present
                var spaceIdx = col.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    var suffix = col[spaceIdx..].Trim().ToUpperInvariant();
                    if (suffix is "ASC" or "DESC")
                    {
                        col = col[..spaceIdx];
                    }
                }
                columns.Add(col);
            }
        }
        return columns;
    }

    /// <summary>
    /// Creates a serialized <see cref="RpcMessage"/> response from a typed payload.
    /// </summary>
    private static RpcMessage CreateResponse<T>(int messageType, T payload, int requestId)
    {
        return new RpcMessage
        {
            MessageType = messageType,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(payload)
        };
    }

    /// <summary>
    /// Loads <see cref="AiSettings"/> from the current configuration.
    /// </summary>
    private static AiSettings LoadAiSettings()
    {
        try
        {
            return ConfigManager.Load().Ai;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load AI settings, using defaults");
            return new AiSettings();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _privacyTransformer.Dispose();
    }
}
