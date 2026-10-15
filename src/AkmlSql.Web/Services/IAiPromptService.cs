using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Ai.Prompts;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M6 task T129; extended by spec 028 (M6) tasks T009/T015/T026.
/// Builds AI prompts using the extracted <c>AkmlSql.AI</c> prompt builders, then sends them
/// via <see cref="IAiClientFactory"/>.
///
/// <para>
/// Schema context is resolved internally per feature via <see cref="IAiSchemaContextProvider"/>,
/// honouring that feature's active privacy disclosure mode (US1). Each feature has a buffered
/// method and a streaming overload; the streaming path yields text deltas (US2), and the
/// buffered method is the fallback when a provider/mode does not stream. The fully-local guard
/// (FR-004/FR-012) is enforced on both paths.
/// </para>
///
/// <para>
/// Spec 042: every prompt is written for the dialect of the active connection (FR-080), and PostgreSQL SQL bound for a
/// cloud provider has its literals removed by the engine first and put back into the answer (FR-081).
/// </para>
/// </summary>
public interface IAiPromptService
{
    Task<string> ExplainAsync(string selectedSql, CancellationToken ct);
    Task<string> FixAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct);

    /// <summary>Fix with the whole error (spec 042, FR-082): a PostgreSQL error carries its SQLSTATE, detail and hint.</summary>
    Task<string> FixAsync(string failingSql, AiErrorInfo error, CancellationToken ct) =>
        FixAsync(failingSql, error.Message, error.Number, ct);

    Task<string> OptimizeAsync(string selectedSql, CancellationToken ct);
    Task<string> TextToSqlAsync(string naturalLanguage, CancellationToken ct);

    /// <summary>Index Analysis (spec 028 T026): suggests CREATE INDEX statements for the query.</summary>
    Task<string> IndexAnalysisAsync(string selectedSql, CancellationToken ct);

    IAsyncEnumerable<string> ExplainStreamAsync(string selectedSql, CancellationToken ct);
    IAsyncEnumerable<string> FixStreamAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct);

    /// <summary>Streaming Fix with the whole error (spec 042, FR-082).</summary>
    IAsyncEnumerable<string> FixStreamAsync(string failingSql, AiErrorInfo error, CancellationToken ct) =>
        FixStreamAsync(failingSql, error.Message, error.Number, ct);

    IAsyncEnumerable<string> OptimizeStreamAsync(string selectedSql, CancellationToken ct);
    IAsyncEnumerable<string> TextToSqlStreamAsync(string naturalLanguage, CancellationToken ct);
    IAsyncEnumerable<string> IndexAnalysisStreamAsync(string selectedSql, CancellationToken ct);
}

internal sealed class AiPromptService : IAiPromptService
{
    private readonly IAiClientFactory _client;
    private readonly IAiPreference _preference;
    private readonly IAiSchemaContextProvider _schema;
    private readonly IAiFeatureSettings _settings;
    private readonly AiOutboundGate _gate;

    /// <param name="dialect">Spec 042: the dialect of the active connection. Null (a host without PostgreSQL support) is SQL Server.</param>
    /// <param name="redactor">Spec 042: removes the literals of PostgreSQL text before it goes to a cloud provider.</param>
    /// <param name="dialogs">Spec 042: asks the user when the literals could not be removed.</param>
    public AiPromptService(
        IAiClientFactory client,
        IAiPreference preference,
        IAiSchemaContextProvider schema,
        IAiFeatureSettings settings,
        IAiDialectContext? dialect = null,
        IPostgresRedactor? redactor = null,
        IDialogService? dialogs = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preference = preference ?? throw new ArgumentNullException(nameof(preference));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _gate = new AiOutboundGate(dialect, redactor, dialogs);
    }

    // ── Buffered ─────────────────────────────────────────────────────────────────────────

    public async Task<string> ExplainAsync(string selectedSql, CancellationToken ct)
    {
        var call = await PrepareAsync("explain", selectedSql, isSql: true, ct).ConfigureAwait(false);
        var (system, user) = await BuildExplainAsync(selectedSql, call.Outbound.Text, ct).ConfigureAwait(false);
        return await SendAsync(call, system, user, ct).ConfigureAwait(false);
    }

    public Task<string> FixAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct) =>
        FixAsync(failingSql, new AiErrorInfo(errorMessage, errorNumber), ct);

    public async Task<string> FixAsync(string failingSql, AiErrorInfo error, CancellationToken ct)
    {
        var call = await PrepareAsync("fix", failingSql, isSql: true, ct).ConfigureAwait(false);
        var (system, user) = await BuildFixAsync(failingSql, call.Outbound, error, ct).ConfigureAwait(false);
        return await SendAsync(call, system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> OptimizeAsync(string selectedSql, CancellationToken ct)
    {
        var call = await PrepareAsync("optimize", selectedSql, isSql: true, ct).ConfigureAwait(false);
        var (system, user) = await BuildOptimizeAsync(selectedSql, call.Outbound.Text, ct).ConfigureAwait(false);
        return await SendAsync(call, system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> TextToSqlAsync(string naturalLanguage, CancellationToken ct)
    {
        // Prose, not SQL: there are no literals to find in it, so it is sent as the user wrote it.
        var call = await PrepareAsync("texttosql", naturalLanguage, isSql: false, ct).ConfigureAwait(false);
        var (system, user) = await BuildTextToSqlAsync(naturalLanguage, ct).ConfigureAwait(false);
        return await SendAsync(call, system, user, ct).ConfigureAwait(false);
    }

    public async Task<string> IndexAnalysisAsync(string selectedSql, CancellationToken ct)
    {
        var call = await PrepareAsync("indexanalysis", selectedSql, isSql: true, ct).ConfigureAwait(false);
        var (system, user) = await BuildIndexAnalysisAsync(selectedSql, call.Outbound.Text, ct).ConfigureAwait(false);
        return await SendAsync(call, system, user, ct).ConfigureAwait(false);
    }

    // ── Streaming ────────────────────────────────────────────────────────────────────────

    public async IAsyncEnumerable<string> ExplainStreamAsync(string selectedSql, [EnumeratorCancellation] CancellationToken ct)
    {
        var call = await PrepareAsync("explain", selectedSql, isSql: true, ct).ConfigureAwait(false);
        var (system, user) = await BuildExplainAsync(selectedSql, call.Outbound.Text, ct).ConfigureAwait(false);
        await foreach (var tok in StreamAsync(call, system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    public IAsyncEnumerable<string> FixStreamAsync(string failingSql, string errorMessage, int errorNumber, CancellationToken ct) =>
        FixStreamAsync(failingSql, new AiErrorInfo(errorMessage, errorNumber), ct);

    public async IAsyncEnumerable<string> FixStreamAsync(string failingSql, AiErrorInfo error, [EnumeratorCancellation] CancellationToken ct)
    {
        var call = await PrepareAsync("fix", failingSql, isSql: true, ct).ConfigureAwait(false);
        var (system, user) = await BuildFixAsync(failingSql, call.Outbound, error, ct).ConfigureAwait(false);
        await foreach (var tok in StreamAsync(call, system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    public async IAsyncEnumerable<string> OptimizeStreamAsync(string selectedSql, [EnumeratorCancellation] CancellationToken ct)
    {
        var call = await PrepareAsync("optimize", selectedSql, isSql: true, ct).ConfigureAwait(false);
        var (system, user) = await BuildOptimizeAsync(selectedSql, call.Outbound.Text, ct).ConfigureAwait(false);
        await foreach (var tok in StreamAsync(call, system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    public async IAsyncEnumerable<string> TextToSqlStreamAsync(string naturalLanguage, [EnumeratorCancellation] CancellationToken ct)
    {
        var call = await PrepareAsync("texttosql", naturalLanguage, isSql: false, ct).ConfigureAwait(false);
        var (system, user) = await BuildTextToSqlAsync(naturalLanguage, ct).ConfigureAwait(false);
        await foreach (var tok in StreamAsync(call, system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    public async IAsyncEnumerable<string> IndexAnalysisStreamAsync(string selectedSql, [EnumeratorCancellation] CancellationToken ct)
    {
        var call = await PrepareAsync("indexanalysis", selectedSql, isSql: true, ct).ConfigureAwait(false);
        var (system, user) = await BuildIndexAnalysisAsync(selectedSql, call.Outbound.Text, ct).ConfigureAwait(false);
        await foreach (var tok in StreamAsync(call, system, user, ct).ConfigureAwait(false)) yield return tok;
    }

    // ── Prompt builders (schema resolved per feature mode; the SQL they carry is what may be sent) ───

    private async Task<(string System, string User)> BuildExplainAsync(string selectedSql, string outboundSql, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("explain", selectedSql, ct).ConfigureAwait(false);
        return ExplainPrompt.Build(schemaText, outboundSql, _gate.Dialect, _gate.ServerVersion);
    }

    private async Task<(string System, string User)> BuildFixAsync(string failingSql, AiOutbound outbound, AiErrorInfo error, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("fix", failingSql, ct).ConfigureAwait(false);
        // The server's error goes the same way as the script (FR-081): with the literals it repeats replaced, and without a detail that could hold row data.
        var sent = outbound.Protect(error);
        return FixPrompt.Build(
            schemaText, outbound.Text, sent.Message, sent.Number, _gate.Dialect, _gate.ServerVersion, sent.SqlState, sent.Detail, sent.Hint);
    }

    private async Task<(string System, string User)> BuildOptimizeAsync(string selectedSql, string outboundSql, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("optimize", selectedSql, ct).ConfigureAwait(false);
        return OptimizePrompt.Build(schemaText, outboundSql, _gate.Dialect, _gate.ServerVersion);
    }

    private async Task<(string System, string User)> BuildTextToSqlAsync(string naturalLanguage, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("texttosql", naturalLanguage, ct).ConfigureAwait(false);
        return TextToSqlPrompt.Build(schemaText, naturalLanguage, _gate.Dialect, _gate.ServerVersion);
    }

    private async Task<(string System, string User)> BuildIndexAnalysisAsync(string selectedSql, string outboundSql, CancellationToken ct)
    {
        var schemaText = await _schema.GetSchemaTextAsync("indexanalysis", selectedSql, ct).ConfigureAwait(false);
        // The browser has no execution plan offline -> pass null (prompt degrades to schema + SQL).
        return IndexAnalysisPrompt.Build(schemaText, outboundSql, executionPlanXml: null, _gate.Dialect, _gate.ServerVersion);
    }

    // ── Provider resolution + fully-local guard + redaction gate + transport ───────────────

    /// <summary>The provider a request goes to and what may be sent to it in place of the user's SQL.</summary>
    private sealed record Call(string ProviderId, AiOutbound Outbound);

    /// <summary>
    /// The provider is resolved (and the fully-local guard applied) before anything else, then the SQL is prepared for it:
    /// PostgreSQL text bound for a cloud provider has its literals removed first (spec 042, FR-081).
    /// </summary>
    private async Task<Call> PrepareAsync(string featureId, string text, bool isSql, CancellationToken ct)
    {
        var providerId = await ResolveProviderAsync(featureId).ConfigureAwait(false);
        var outbound = isSql
            ? await _gate.PrepareSqlAsync(providerId, text, askUser: true, ct).ConfigureAwait(false)
            : AiOutbound.Unchanged(text);
        return new Call(providerId, outbound);
    }

    private async Task<string> ResolveProviderAsync(string featureId)
    {
        var providerId = await _preference.GetActiveAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(providerId))
        {
            throw new InvalidOperationException(
                "No active AI provider. Open Settings -> AI and add a provider with an API key.");
        }

        // FR-004 / FR-012 fully-local guard, enforced at the send path on both buffered and
        // streaming calls so a per-feature override (or a global-mode flip while a cloud
        // provider is active) can never leak to a cloud provider.
        var mode = await _settings.ResolveModeAsync(featureId).ConfigureAwait(false);
        if (mode == AiPrivacyMode.FullyLocal && !AiProviders.IsLocal(providerId))
        {
            throw new InvalidOperationException(
                $"'{featureId}' is set to Fully local, but the active provider '{providerId}' is not local. " +
                "Switch to Ollama or LM Studio, or change the privacy mode in Settings -> AI.");
        }

        return providerId;
    }

    private async Task<string> SendAsync(Call call, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var answer = await _client.SendAsync(call.ProviderId, new AiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
        }, ct).ConfigureAwait(false);
        return call.Outbound.Restore(answer);
    }

    private IAsyncEnumerable<string> StreamAsync(Call call, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var stream = _client.StreamAsync(call.ProviderId, new AiChatRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
        }, ct);
        return call.Outbound.RestoreStream(stream, ct);
    }
}
