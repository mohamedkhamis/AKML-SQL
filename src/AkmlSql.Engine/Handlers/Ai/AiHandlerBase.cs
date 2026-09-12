using System.Diagnostics;
using AkmlSql.Core.Config;
using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Transports;
using Serilog;

namespace AkmlSql.Engine.Handlers.Ai;

/// <summary>
/// Spec 022 (M0 closure) -- P3 / US3. Abstract base for every AI message handler. Lifts ALL
/// the boilerplate shared across the pre-closure <c>AiRequestHandler.HandleXxxAsync</c> methods:
/// privacy-consent gate, settings retrieval, Stopwatch ownership, and the three catch blocks
/// (consent / cancellation / generic exception) that map to a typed error response via
/// <see cref="BuildErrorResponse"/>.
///
/// <para>Concrete subclasses override only <see cref="InvokeAsync"/> with the happy-path
/// per-message logic plus <see cref="BuildErrorResponse"/> with the typed error envelope, plus
/// the two integer-code properties and the <see cref="Feature"/> declaration. They do NOT catch
/// their own exceptions -- the base does it.</para>
///
/// <para>Per FR-013: settings are read fresh on every call via <see cref="AiPipelineServices.SettingsProvider"/>;
/// the consent gate uses the local-provider allowlist (<c>ollama</c>, <c>lmstudio</c>) plus the
/// <see cref="AiSettings.PrivacyConsentRequired"/> flag. Spec 037 (US4): the base resolves the
/// feature's agent and projects it BEFORE the consent gate, so consent is evaluated against the
/// provider that will actually be called.</para>
/// </summary>
public abstract class AiHandlerBase<TRequest, TResponse> : IRpcRequestHandler<TRequest, TResponse>
    where TResponse : new()
{
    protected AiPipelineServices Services { get; }

    protected AiHandlerBase(AiPipelineServices services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public abstract int RequestMessageType { get; }
    public abstract int ResponseMessageType { get; }
    public virtual bool SwallowCancellation => false;
    public virtual bool AllowsEmptyPayload => false;

    /// <summary>Spec 037 (US4, T070): the feature this handler serves — the key for per-feature
    /// agent resolution in <see cref="HandleAsync"/>. One line per concrete handler.</summary>
    public abstract AiFeature Feature { get; }

    /// <summary>Per-message logic. Throw on errors -- the base catches and routes to
    /// <see cref="BuildErrorResponse"/>. Receives the live <see cref="AiSettings"/> — PROJECTED
    /// from <paramref name="resolvedAgent"/> when one resolved (spec 037 US4: connection fields
    /// and request parameters are the agent's; every global concern is preserved) — plus the
    /// resolved agent itself for attribution and the fallback chain, plus the per-call
    /// <see cref="Stopwatch"/> so the success-path response can read LatencyMs.</summary>
    protected abstract Task<TResponse> InvokeAsync(
        TRequest request, RpcContext ctx, AiSettings settings, AiAgent? resolvedAgent, Stopwatch sw, CancellationToken ct);

    /// <summary>Shape the typed error response from a message + elapsed milliseconds. Subclasses
    /// typically return <c>new() { Success = false, ErrorMessage = message, LatencyMs = (int)elapsedMs }</c>.</summary>
    protected abstract TResponse BuildErrorResponse(string errorMessage, long elapsedMs);

    public async Task<TResponse> HandleAsync(TRequest request, RpcContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        AiSettings settings;
        AiAgent? resolvedAgent;
        try
        {
            var globalSettings = Services.SettingsProvider();
            // Spec 037 (US4, contracts/agent-resolution.md Part 1): resolve the feature's agent
            // and project BEFORE the consent gate — consent must be evaluated against the
            // provider that will actually be called. A null resolution runs against the
            // unprojected globals, whose Enabled is false (V19), so the handlers' existing
            // "AI assistance is disabled" guard fires with no new error path.
            resolvedAgent = AiAgentResolver.ResolveFor(globalSettings, Feature);
            settings = resolvedAgent == null
                ? globalSettings
                : AiAgentResolver.Project(globalSettings, resolvedAgent);
            CheckPrivacyConsent(settings);
            // AFTER the consent gate: a request killed by consent must not burn the
            // once-per-feature notice flag.
            NoteAssignedAgentFallback(globalSettings, resolvedAgent);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            Log.Information("{Handler}: privacy consent required", GetType().Name);
            return BuildErrorResponse(consentEx.Message, sw.ElapsedMilliseconds);
        }

        try
        {
            return await InvokeAsync(request, ctx, settings, resolvedAgent, sw, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (SwallowCancellation)
        {
            return new TResponse();
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Log.Debug("{Handler}: cancelled after {LatencyMs}ms", GetType().Name, sw.ElapsedMilliseconds);
            return BuildErrorResponse("Request was cancelled", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "{Handler} failed after {LatencyMs}ms", GetType().Name, sw.ElapsedMilliseconds);
            return BuildErrorResponse(ex.Message, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// FR-049 / V22: an assigned agent that is missing or unusable falls back to the active
    /// agent — logged at Information ONCE per feature per engine process (the dedupe lives in
    /// <see cref="AiPipelineServices"/>, which the registry builds once per process), never per
    /// request. Two signals say the assignment is broken: the production one (V16 cleared the
    /// dangling assignment on load and recorded the feature in
    /// <see cref="AiSettings.ClearedFeatureAssignments"/>) and the legacy one (a dangling
    /// assigned id still present — settings that never went through <c>Normalize</c>, e.g. a
    /// test host). When no active agent is usable either, the log says so instead of naming a
    /// fallback that does not exist.
    /// </summary>
    private void NoteAssignedAgentFallback(AiSettings settings, AiAgent? resolvedAgent)
    {
        var assignedId = AiAgentResolver.AssignedIdFor(settings, Feature);
        if (!string.IsNullOrEmpty(assignedId) && resolvedAgent != null
            && string.Equals(resolvedAgent.Id, assignedId, StringComparison.Ordinal))
            return;   // the assigned agent itself is serving — nothing to notice
        var clearedOnLoad = settings.ClearedFeatureAssignments.Contains(Feature.ToString());
        if (string.IsNullOrEmpty(assignedId) && !clearedOnLoad) return;
        if (!Services.MarkAssignmentFallbackNoticed(Feature)) return;

        if (resolvedAgent != null)
        {
            Log.Information(
                "{Handler}: the agent assigned to {Feature} ({AgentId}) is missing or unusable; falling back to the active agent",
                GetType().Name, Feature, assignedId);
        }
        else
        {
            Log.Information(
                "{Handler}: the agent assigned to {Feature} ({AgentId}) is missing or unusable and no active agent is usable; no fallback is available",
                GetType().Name, Feature, assignedId);
        }
    }

    // ───────── Privacy-consent gate (lifted from AiRequestHandler.CheckPrivacyConsent) ─────────

    private static void CheckPrivacyConsent(AiSettings settings)
    {
        // The predicate (local-provider allowlist + consent flag) is shared with the fallback
        // chain — see AiPipelineServices.PrivacyConsentBlocks. The gate's behaviour is unchanged.
        if (!AiPipelineServices.PrivacyConsentBlocks(settings)) return;
        var provider = settings.Provider?.Trim() ?? string.Empty;
        var providerDisplay = string.IsNullOrEmpty(provider) ? "your AI provider" : provider;
        throw new PrivacyConsentRequiredException(
            $"CONSENT_REQUIRED:Data will be sent to {providerDisplay}. Please confirm in settings.");
    }
}
