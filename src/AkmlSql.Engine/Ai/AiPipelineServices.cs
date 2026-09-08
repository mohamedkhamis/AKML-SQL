using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine.Ai.Context;
using AkmlSql.Engine.Ai.Privacy;
using AkmlSql.Engine.Ai.Providers;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using Microsoft.Extensions.AI;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Ai;

/// <summary>
/// Spec 022 (M0 closure) -- P3 / US3. Shared collaborators for the AI handler subclasses.
/// Carved out of the 1896-LOC <c>AiRequestHandler</c> monolith so the concrete per-message
/// handler classes (<c>AiTextToSqlHandler</c>, <c>AiExplainHandler</c>, ...) can stay focused
/// on per-message logic.
///
/// <para>Per the spec FR-013 invariant: AI settings are read through <see cref="SettingsProvider"/>
/// on every handler call -- this routes through <c>RpcContext.EnsureSettings().Ai</c> so the
/// settings-cache invalidation from <c>AnalysisSettingsChanged</c> propagates without a
/// per-handler refresh hook.</para>
/// </summary>
public sealed class AiPipelineServices : IDisposable
{
    /// <summary>Maximum backoff delay for retry logic. Lifted from AiRequestHandler.</summary>
    private const int MaxBackoffMs = 30_000;

    public required SchemaContextBuilder SchemaContext { get; init; }
    public required PrivacyTransformer Privacy { get; init; }
    public required TsqlParserService Parser { get; init; }

    /// <summary>Fresh-AI-settings provider. Wired to <c>ctx.EnsureSettings().Ai</c> by the registry.</summary>
    public required Func<AiSettings> SettingsProvider { get; init; }

    /// <summary>
    /// Spec 037 (US4, T072): construction seam between the pipeline and
    /// <see cref="AiProviderFactory.Create"/>, so the fallback chain can be exercised without a
    /// network. Receives the PROJECTED settings of the agent being attempted.
    /// </summary>
    public Func<AiSettings, IChatClient> ClientFactory { get; set; } = AiProviderFactory.Create;

    /// <summary>
    /// Spec 037 (US4, T072): the offline-provider construction seam — same role as
    /// <see cref="ClientFactory"/> for <see cref="AiProviderFactory.CreateFromFallback"/>.
    /// </summary>
    public Func<AiSettings, IChatClient> FallbackClientFactory { get; set; } = AiProviderFactory.CreateFromFallback;

    // Spec 037 (US4, T071, V22): features whose "assigned agent unusable — active answers"
    // notice has already been logged. This services instance is process-wide (the registry
    // builds it once), which is exactly the contract's once-per-feature-per-engine-process
    // scope — and it keeps the flag out of the singleton handler instances.
    private readonly ConcurrentDictionary<AiFeature, byte> _assignmentFallbackNoticed = new();

    /// <summary>
    /// V22 — returns <c>true</c> the FIRST time a feature marks its assigned-agent fallback
    /// notice, <c>false</c> on every later call, so the engine logs it once per feature per
    /// engine process rather than once per request (a ghost-text user must not get a
    /// notification storm at every keystroke).
    /// </summary>
    internal bool MarkAssignmentFallbackNoticed(AiFeature feature)
        => _assignmentFallbackNoticed.TryAdd(feature, 0);

    /// <summary>Builds the shared services. Used by <see cref="EngineHandlerRegistry"/>.</summary>
    public static AiPipelineServices Build(
        SchemaCacheManager schemaCache,
        TsqlParserService parser,
        Func<AiSettings> settingsProvider)
    {
        ArgumentNullException.ThrowIfNull(schemaCache);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(settingsProvider);

        return new AiPipelineServices
        {
            SchemaContext = new SchemaContextBuilder(
                // Engine caches are keyed by SESSION ID (ConnectionChangedHandler creates them
                // via GetOrCreateCache(request.SessionId, ...)) — the first argument is the
                // session id, not a connection string.
                (sessionId, db) => schemaCache.GetCache(sessionId, db)),
            Privacy = new PrivacyTransformer(parser),
            Parser = parser,
            SettingsProvider = settingsProvider,
        };
    }

    // ───────── Retry-with-backoff (lifted from AiRequestHandler.ExecuteWithRetryAsync) ─────────

    /// <summary>
    /// Executes <paramref name="action"/> with exponential backoff on HTTP 429 (rate-limited)
    /// responses. Retries are bounded by BOTH <paramref name="maxRetries"/> and
    /// <paramref name="retryBudget"/> (wall-clock across attempts + delays): a quota-exhausted
    /// key returns 429 for every attempt, and unbounded retrying once ground for 318 s — past
    /// the provider timeout AND the shell's IPC wait, so the user saw "A task was canceled"
    /// instead of the provider's actual "You exceeded your current quota" message. When the
    /// budget is spent the last provider error is rethrown untouched.
    /// </summary>
    public static async Task<T> ExecuteWithBackoffAsync<T>(
        Func<Task<T>> action, int maxRetries, CancellationToken ct, TimeSpan? retryBudget = null)
    {
        var budget = retryBudget ?? System.Threading.Timeout.InfiniteTimeSpan;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (
                attempt < maxRetries &&
                ex.StatusCode == HttpStatusCode.TooManyRequests &&
                (budget == System.Threading.Timeout.InfiniteTimeSpan || sw.Elapsed < budget))
            {
                attempt++;
                var delayMs = Math.Min((int)Math.Pow(2, attempt) * 1000, MaxBackoffMs);
                if (budget != System.Threading.Timeout.InfiniteTimeSpan)
                {
                    var remainingMs = (budget - sw.Elapsed).TotalMilliseconds;
                    delayMs = Math.Min(delayMs, Math.Max(0, (int)remainingMs));
                }
                Log.Warning("AI request rate-limited (429), retry {Attempt}/{MaxRetries} after {DelayMs}ms",
                    attempt, maxRetries, delayMs);
                await Task.Delay(delayMs, ct);
            }
        }
    }

    // ───────── Privacy-consent predicate (shared with AiHandlerBase's gate) ─────────

    /// <summary>The local providers the consent gate allow-lists — consent is about data
    /// leaving the machine. Shared by <c>AiHandlerBase.CheckPrivacyConsent</c> and the
    /// fallback chain below.</summary>
    private static readonly HashSet<string> LocalProviders =
        new(StringComparer.OrdinalIgnoreCase) { AiProviderIds.Ollama, AiProviderIds.LmStudio };

    /// <summary>
    /// The predicate behind the privacy-consent gate: <c>true</c> when calling the provider
    /// named by <paramref name="settings"/> would send data somewhere the user has not
    /// consented to. Evaluated against the ATTEMPTING agent's projected settings, so consent is
    /// always judged against the provider that will actually be called
    /// (contracts/agent-resolution.md Part 1) — including fallback-chain candidates.
    /// </summary>
    internal static bool PrivacyConsentBlocks(AiSettings settings)
    {
        if (!settings.PrivacyConsentRequired) return false;
        var provider = settings.Provider?.Trim() ?? string.Empty;
        return !LocalProviders.Contains(provider);
    }

    // ───────── Primary → fallback chain → offline last resort (spec 037 US4, T072/T073) ─────────

    /// <summary>The per-attempt retry budget (contract invariant 2): retries must never make one
    /// logical request outlive the deadline the rest of the pipeline (and the shell's IPC wait,
    /// provider timeout + margin) is built around — <c>max(30s, agent.Timeout)</c>, computed from
    /// the ATTEMPTING agent's own projected settings.</summary>
    internal static TimeSpan RetryBudgetFor(AiSettings settings)
        => TimeSpan.FromSeconds(Math.Max(30, settings.Timeout));

    /// <summary>
    /// Calls the selected agent's provider with retry-on-rate-limit. On transient failure
    /// (anything other than cancellation / consent) walks <see cref="AiSettings.FallbackOrder"/>
    /// in order — usable agents only, the selected agent never in its own chain, and any
    /// candidate whose provider the user has not consented to skipped without being called —
    /// and falls through to the configured offline provider last (FR-050/FR-051). The chain
    /// stops at the first success and returns the answering agent's name (FR-052) plus whether
    /// a fallback fired. Lifted from <c>AiRequestHandler.ExecuteWithFallbackAsync</c>; the
    /// chain is "a list in front of the existing single fallback" (research R5), so a
    /// configuration that sets only <see cref="AiSettings.OfflineProvider"/> behaves exactly as
    /// it always has.
    /// </summary>
    /// <param name="settings">The selected agent's PROJECTED settings (the base handler has
    /// already resolved and projected). Its <c>Agents</c>/<c>FallbackOrder</c> copies drive the
    /// chain; projection preserves every global concern, so re-projecting a chain candidate from
    /// it is equivalent to projecting from the globals.</param>
    /// <param name="selectedAgent">The agent <paramref name="settings"/> was projected from, or
    /// <c>null</c> for a pre-resolution caller (legacy flat settings).</param>
    public async Task<(ChatResponse Response, bool UsedFallback, string? AgentName)> ExecuteWithFallbackAsync(
        AiSettings settings,
        AiAgent? selectedAgent,
        List<ChatMessage> messages,
        ChatOptions options,
        CancellationToken ct)
    {
        // The user's chain (FR-050): usable agents named by FallbackOrder, in order, skipping
        // the selected agent — an agent is never its own fallback (contract invariant 5).
        var chain = new List<AiAgent>();
        if (settings.FallbackOrder != null)
        {
            foreach (var id in settings.FallbackOrder)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (selectedAgent != null && string.Equals(id, selectedAgent.Id, StringComparison.Ordinal)) continue;
                var candidate = settings.Agents?.FirstOrDefault(
                    a => a != null && string.Equals(a.Id, id, StringComparison.Ordinal));
                if (candidate == null || !AiAgentResolver.IsUsable(candidate)) continue;
                chain.Add(candidate);
            }
        }

        // When nothing can serve as a fallback the primary's exception must propagate UNTOUCHED
        // (the pre-chain behaviour, encoded by the original filter's OfflineProvider clause);
        // the two exception-type clauses are preserved verbatim at every attempt: cancellation
        // and consent never trigger a fallback.
        var hasFallback = chain.Count > 0 || !string.IsNullOrWhiteSpace(settings.OfflineProvider);
        // FR-057: the all-fail aggregate names every attempt in order (the primary by its agent
        // name when it has one). FR-058: the per-attempt text surfaced to the user is the
        // FR-014 taxonomy message, never the raw provider payload — the raw exception rides
        // along inside the inner exception and is attached to the log event.
        var attemptNames = new List<string>();
        var attemptExceptions = new List<Exception>();
        var primaryName = !string.IsNullOrEmpty(selectedAgent?.Name) ? selectedAgent!.Name : null;

        try
        {
            using var primaryClient = ClientFactory(settings);
            var response = await ExecuteWithBackoffAsync(
                () => primaryClient.GetResponseAsync(messages, options, ct),
                settings.Retries, ct, retryBudget: RetryBudgetFor(settings));
            return (response, false, selectedAgent?.Name);
        }
        catch (Exception ex) when (
            ex is not OperationCanceledException &&
            ex is not PrivacyConsentRequiredException &&
            hasFallback)
        {
            attemptNames.Add(primaryName ?? "primary provider");
            RecordAttemptFailure(ex, settings,
                primaryName != null ? $"Agent '{primaryName}'" : "Primary provider",
                "walking the fallback chain", attemptExceptions);
        }

        foreach (var candidate in chain)
        {
            var projected = AiAgentResolver.Project(settings, candidate);
            // Consent is evaluated against the provider that will actually be called — a chain
            // candidate the user has not consented to is SKIPPED (never called), and the chain
            // walks on. The user already made the request against a consented primary; the
            // chain must not leak their data to a non-consented cloud.
            if (PrivacyConsentBlocks(projected))
            {
                Log.Warning(
                    "AI fallback chain: skipping agent {AgentName} — privacy consent has not been granted for provider {Provider}",
                    candidate.Name, candidate.Provider);
                continue;
            }
            try
            {
                using var client = ClientFactory(projected);
                var response = await ExecuteWithBackoffAsync(
                    () => client.GetResponseAsync(messages, options, ct),
                    projected.Retries, ct, retryBudget: RetryBudgetFor(projected));
                Log.Warning("AI fallback chain: agent {AgentName} answered after the primary failed", candidate.Name);
                return (response, true, candidate.Name);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException &&
                ex is not PrivacyConsentRequiredException)
            {
                attemptNames.Add(candidate.Name);
                RecordAttemptFailure(ex, projected, $"Fallback agent '{candidate.Name}'",
                    "trying the next candidate", attemptExceptions);
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.OfflineProvider))
        {
            Log.Warning("Primary AI provider failed, switching to offline fallback provider={FallbackProvider}",
                settings.OfflineProvider);
            try
            {
                using var fallbackClient = FallbackClientFactory(settings);
                var response = await fallbackClient.GetResponseAsync(messages, options, ct);
                return (response, true, AiProviderIds.DisplayName(settings.OfflineProvider));
            }
            catch (Exception fallbackEx) when (
                fallbackEx is not OperationCanceledException &&
                fallbackEx is not PrivacyConsentRequiredException)
            {
                var offlineName = AiProviderIds.DisplayName(settings.OfflineProvider);
                attemptNames.Add(offlineName);
                var sanitized = AiFailureTaxonomy.Map(fallbackEx,
                    settings.OfflineProvider ?? "", settings.OfflineModel ?? "",
                    settings.OfflineEndpoint, settings.Timeout, "The AI request");
                Log.Error(fallbackEx, "Offline fallback provider also failed: {SanitizedFailure}", sanitized);
                attemptExceptions.Add(new Exception($"Offline fallback '{offlineName}' failed: {sanitized}", fallbackEx));
            }
        }

        // AggregateException.Message re-appends each inner exception's message, so the summary
        // stays concise (how many were tried, who, in what order) and the per-attempt detail
        // lives on the inner exceptions — otherwise the chat panel's ErrorMessage shows every
        // attempt's text twice.
        var summary = attemptNames.Count == 1
            ? $"The AI request failed — {attemptNames[0]} was the only provider available."
            : $"All {attemptNames.Count} AI providers failed (tried in order: {string.Join(" → ", attemptNames)}).";
        throw new AggregateException(summary, attemptExceptions);
    }

    /// <summary>
    /// FR-058: maps one failed attempt to the FR-014 taxonomy (<see cref="AiFailureTaxonomy"/>)
    /// for the user-facing aggregate and the "failed" log line. The recorded inner exception
    /// carries the SANITIZED text as its message with the raw exception preserved inside (and
    /// on the log event) for diagnosis — key material in a provider's raw payload never reaches
    /// a user-facing string.
    /// </summary>
    private static void RecordAttemptFailure(
        Exception ex, AiSettings attempted, string agentLabel, string nextStep, List<Exception> attemptExceptions)
    {
        var sanitized = AiFailureTaxonomy.Map(ex,
            attempted.Provider ?? "", attempted.Model ?? "", attempted.Endpoint,
            attempted.Timeout, "The AI request");
        Log.Warning(ex, "AI fallback chain: {AgentLabel} failed: {SanitizedFailure} — {NextStep}",
            agentLabel, sanitized, nextStep);
        attemptExceptions.Add(new Exception($"{agentLabel} failed: {sanitized}", ex));
    }

    // ───────── Generated-SQL validation (lifted from AiRequestHandler.ValidateGeneratedSql) ─────────

    /// <summary>
    /// Validates AI-generated SQL by parsing it and cross-checking every table/view reference
    /// against the provided schema context. Returns annotations for objects not present in the
    /// current schema. Lifted from <c>AiRequestHandler.ValidateGeneratedSql</c>.
    /// </summary>
    public List<AnnotationDto> ValidateGeneratedSql(string sql, SchemaContext context)
    {
        var annotations = new List<AnnotationDto>();
        if (string.IsNullOrWhiteSpace(sql) || context.Objects.Count == 0)
            return annotations;

        try
        {
            var script = Parser.Parse(sql, out _);
            if (script == null) return annotations;

            var knownObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in context.Objects)
            {
                knownObjects.Add($"{obj.Schema}.{obj.Name}");
                if (obj.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                    knownObjects.Add(obj.Name);
            }

            var visitor = new TableReferenceVisitor();
            script.Accept(visitor);

            foreach (var tableRef in visitor.TableReferences)
            {
                var objectName = tableRef.SchemaObject;
                if (objectName == null) continue;
                var schemaName = objectName.SchemaIdentifier?.Value ?? "dbo";
                var tableName = objectName.BaseIdentifier?.Value;
                if (string.IsNullOrEmpty(tableName)) continue;
                var qualifiedName = $"{schemaName}.{tableName}";
                if (tableName.StartsWith("#") || tableName.StartsWith("@")) continue;
                if (!knownObjects.Contains(qualifiedName) && !knownObjects.Contains(tableName))
                {
                    annotations.Add(new AnnotationDto
                    {
                        StartLine = tableRef.StartLine,
                        EndLine = tableRef.StartLine,
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
    /// Strips markdown code fences (```sql ... ```) from AI responses. Some models include
    /// fences despite being instructed not to. Lifted from <c>AiRequestHandler.StripCodeFences</c>.
    /// </summary>
    public static string StripCodeFences(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return text;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
        if (trimmed.EndsWith("```", StringComparison.Ordinal)) trimmed = trimmed[..^3];
        return trimmed.Trim();
    }

    // ───────── Response-section parsing (lifted from AiRequestHandler) ─────────

    /// <summary>Extracts content under a named section header (case-insensitive); stops at the next
    /// known section header. Lifted from <c>AiRequestHandler.ExtractSection</c>.</summary>
    public static string ExtractSection(string responseText, string sectionHeader)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return string.Empty;
        var startIdx = responseText.IndexOf(sectionHeader, StringComparison.OrdinalIgnoreCase);
        if (startIdx < 0) return string.Empty;
        var contentStart = startIdx + sectionHeader.Length;
        string[] knownHeaders =
        [
            "OPTIMIZED SQL:", "EXPLANATION:", "ANNOTATIONS:", "INDEX SUGGESTIONS:",
            "SUMMARY:", "PURPOSE:", "STEP BY STEP:", "KEY DETAILS:", "SUGGESTIONS:",
            "FIXED SQL:"
        ];
        var endIdx = responseText.Length;
        foreach (var header in knownHeaders)
        {
            if (string.Equals(header, sectionHeader, StringComparison.OrdinalIgnoreCase)) continue;
            var headerIdx = responseText.IndexOf(header, contentStart, StringComparison.OrdinalIgnoreCase);
            if (headerIdx >= 0 && headerIdx < endIdx) endIdx = headerIdx;
        }
        return responseText[contentStart..endIdx].Trim();
    }

    /// <summary>Splits an AiExplain response into Purpose / StepByStep / KeyDetails / Suggestions
    /// sections. Lifted from <c>AiRequestHandler.ParseExplainSections</c>.</summary>
    public static (string purpose, string stepByStep, string keyDetails, string suggestions)
        ParseExplainSections(string responseText)
    {
        var purpose = ""; var stepByStep = ""; var keyDetails = ""; var suggestions = "";
        if (string.IsNullOrWhiteSpace(responseText)) return (purpose, stepByStep, keyDetails, suggestions);
        var headers = new[] { "PURPOSE:", "STEP BY STEP:", "KEY DETAILS:", "SUGGESTIONS:" };
        var positions = new List<(int index, string header)>();
        foreach (var header in headers)
        {
            var idx = responseText.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) positions.Add((idx, header));
        }
        positions.Sort((a, b) => a.index.CompareTo(b.index));
        for (int i = 0; i < positions.Count; i++)
        {
            var start = positions[i].index + positions[i].header.Length;
            var end = i + 1 < positions.Count ? positions[i + 1].index : responseText.Length;
            var content = responseText[start..end].Trim();
            switch (positions[i].header.ToUpperInvariant())
            {
                case "PURPOSE:": purpose = content; break;
                case "STEP BY STEP:": stepByStep = content; break;
                case "KEY DETAILS:": keyDetails = content; break;
                case "SUGGESTIONS:": suggestions = content; break;
            }
        }
        if (positions.Count == 0) purpose = responseText.Trim();
        return (purpose, stepByStep, keyDetails, suggestions);
    }

    /// <summary>Fallback parser when an AiFix response omits the expected section headers.</summary>
    public static (string fixedSql, string explanation) ParseFixSectionsFallback(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText)) return ("", "");
        var explanationIdx = responseText.IndexOf("EXPLANATION:", StringComparison.OrdinalIgnoreCase);
        if (explanationIdx >= 0)
            return (responseText[..explanationIdx].Trim(),
                    responseText[(explanationIdx + "EXPLANATION:".Length)..].Trim());
        return (responseText.Trim(), "");
    }

    /// <summary>Diff-style line annotations between original SQL and AI-fixed SQL.
    /// Lifted from <c>AiRequestHandler.BuildDiffAnnotations</c>.</summary>
    public static List<AnnotationDto> BuildDiffAnnotations(string originalSql, string fixedSql)
    {
        var result = new List<AnnotationDto>();
        if (string.IsNullOrEmpty(originalSql) || string.IsNullOrEmpty(fixedSql)) return result;
        var origLines = originalSql.Split('\n');
        var fixLines = fixedSql.Split('\n');
        var maxLines = Math.Max(origLines.Length, fixLines.Length);
        for (int i = 0; i < maxLines; i++)
        {
            var o = i < origLines.Length ? origLines[i].TrimEnd('\r') : "";
            var f = i < fixLines.Length ? fixLines[i].TrimEnd('\r') : "";
            if (string.Equals(o, f, StringComparison.Ordinal)) continue;
            var ot = o.Trim(); var ft = f.Trim();
            string cat, desc;
            if (string.Equals(ot, ft, StringComparison.OrdinalIgnoreCase)) { cat = "safe"; desc = "Whitespace or casing change"; }
            else if (string.IsNullOrEmpty(ot) && !string.IsNullOrEmpty(ft)) { cat = "review"; desc = "New line added"; }
            else if (!string.IsNullOrEmpty(ot) && string.IsNullOrEmpty(ft)) { cat = "review"; desc = "Line removed"; }
            else { cat = "review"; desc = "Structural change"; }
            result.Add(new AnnotationDto { StartLine = i + 1, EndLine = i + 1, Category = cat, Description = desc });
        }
        return result;
    }

    /// <summary>Parses `[SAFE|REVIEW] Line N(-M)?: description` lines from an AI annotations block.</summary>
    public static List<AnnotationDto> ParseAnnotations(string annotationsText)
    {
        var annotations = new List<AnnotationDto>();
        if (string.IsNullOrWhiteSpace(annotationsText)) return annotations;
        var regex = new Regex(@"\[(SAFE|REVIEW)\]\s*Line\s+(\d+)(?:\s*[-–]\s*(\d+))?\s*:\s*(.+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        foreach (var line in annotationsText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var match = regex.Match(line.Trim());
            if (!match.Success) continue;
            var startLine = int.TryParse(match.Groups[2].Value, out var sl) ? sl : 0;
            var endLine = match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out var el) ? el : startLine;
            annotations.Add(new AnnotationDto
            {
                StartLine = startLine, EndLine = endLine,
                Category = match.Groups[1].Value.ToLowerInvariant(),
                Description = match.Groups[4].Value.Trim(),
            });
        }
        return annotations;
    }

    private static readonly Regex CreateIndexRegex = new(
        @"CREATE\s+(?:NONCLUSTERED\s+)?INDEX\s+\[?([^\]\s]+)\]?\s+ON\s+\[?([^\]\s(]+)\]?\s*\(\s*([^)]+)\s*\)(?:\s*INCLUDE\s*\(\s*([^)]+)\s*\))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parses CREATE INDEX statements + trailing metadata from an AI response.
    /// Lifted from <c>AiRequestHandler.ParseIndexSuggestions</c>.</summary>
    public static List<IndexSuggestionDto> ParseIndexSuggestions(string indexText)
    {
        var suggestions = new List<IndexSuggestionDto>();
        if (string.IsNullOrWhiteSpace(indexText)) return suggestions;
        if (indexText.Trim().StartsWith("None", StringComparison.OrdinalIgnoreCase)) return suggestions;

        var lines = indexText.Split('\n');
        var current = "";
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(current)) TryParseIndexStatement(current, suggestions);
                current = line;
            }
            else if (!string.IsNullOrEmpty(current)) current += " " + line;
        }
        if (!string.IsNullOrEmpty(current)) TryParseIndexStatement(current, suggestions);
        return suggestions;
    }

    private static void TryParseIndexStatement(string statement, List<IndexSuggestionDto> suggestions)
    {
        var match = CreateIndexRegex.Match(statement);
        if (!match.Success) return;
        var tableName = match.Groups[2].Value.Trim().Trim('[', ']');
        var keyColumns = ParseColumnList(match.Groups[3].Value);
        var includeColumns = match.Groups[4].Success ? ParseColumnList(match.Groups[4].Value) : null;
        var commentIdx = statement.IndexOf("--", StringComparison.Ordinal);
        var createScript = commentIdx >= 0 ? statement[..commentIdx].Trim() : statement.Trim();
        var improvement = ""; var estimatedSizeKb = 0; var writeImpact = "";
        if (commentIdx >= 0)
        {
            var c = statement[(commentIdx + 2)..];
            var im = Regex.Match(c, @"Improvement:\s*~?(\d+)%", RegexOptions.IgnoreCase);
            if (im.Success) improvement = $"~{im.Groups[1].Value}% faster reads";
            var sm = Regex.Match(c, @"Size:\s*~?(\d+)\s*KB", RegexOptions.IgnoreCase);
            if (sm.Success && int.TryParse(sm.Groups[1].Value, out var s)) estimatedSizeKb = s;
            var wm = Regex.Match(c, @"Write\s+Impact:\s*(Low|Medium|High)", RegexOptions.IgnoreCase);
            if (wm.Success) writeImpact = wm.Groups[1].Value;
        }
        suggestions.Add(new IndexSuggestionDto
        {
            CreateScript = createScript, TableName = tableName, Columns = keyColumns,
            IncludeColumns = includeColumns, EstimatedImprovement = improvement,
            EstimatedSizeKb = estimatedSizeKb, WriteImpact = writeImpact,
        });
    }

    private static List<string> ParseColumnList(string columnsRaw)
    {
        var columns = new List<string>();
        foreach (var part in columnsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var col = part.Trim().Trim('[', ']').Trim();
            if (string.IsNullOrEmpty(col)) continue;
            var spaceIdx = col.IndexOf(' ');
            if (spaceIdx > 0)
            {
                var suffix = col[spaceIdx..].Trim().ToUpperInvariant();
                if (suffix is "ASC" or "DESC") col = col[..spaceIdx];
            }
            columns.Add(col);
        }
        return columns;
    }

    private static readonly Regex SqlCodeBlockPattern = new(
        @"```sql\s*\n(.*?)```",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>Extracts SQL code blocks from an AiChat response and builds copy-to-clipboard
    /// <see cref="CodeActionDto"/> entries. Lifted from <c>AiRequestHandler.ExtractCodeActions</c>.</summary>
    public static List<CodeActionDto> ExtractCodeActions(string responseText)
    {
        var actions = new List<CodeActionDto>();
        if (string.IsNullOrWhiteSpace(responseText)) return actions;
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
                        ActionType = "copyToClipboard", Code = sql,
                    });
                }
            }
        }
        return actions;
    }

    public void Dispose() => Privacy.Dispose();

    private sealed class TableReferenceVisitor : TSqlFragmentVisitor
    {
        public List<NamedTableReference> TableReferences { get; } = new();
        public override void Visit(NamedTableReference node)
        {
            TableReferences.Add(node);
            base.Visit(node);
        }
    }
}

/// <summary>
/// Exception thrown when privacy consent is required but has not been given.
/// Lifted from <c>AiRequestHandler.PrivacyConsentRequiredException</c> (was private sealed there).
/// Now public so <see cref="AkmlSql.Engine.Handlers.Ai.AiHandlerBase{TRequest, TResponse}"/>
/// can throw it and concrete subclasses can catch it for per-message error envelopes.
/// </summary>
public sealed class PrivacyConsentRequiredException : Exception
{
    public PrivacyConsentRequiredException(string message) : base(message) { }
}
