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
/// the two integer-code properties. They do NOT catch their own exceptions -- the base does it.</para>
///
/// <para>Per FR-013: settings are read fresh on every call via <see cref="AiPipelineServices.SettingsProvider"/>;
/// the consent gate uses the local-provider allowlist (<c>ollama</c>, <c>lmstudio</c>) plus the
/// <see cref="AiSettings.PrivacyConsentRequired"/> flag.</para>
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

    /// <summary>Per-message logic. Throw on errors -- the base catches and routes to
    /// <see cref="BuildErrorResponse"/>. Receives the live <see cref="AiSettings"/> + the
    /// per-call <see cref="Stopwatch"/> so the success-path response can read LatencyMs.</summary>
    protected abstract Task<TResponse> InvokeAsync(
        TRequest request, RpcContext ctx, AiSettings settings, Stopwatch sw, CancellationToken ct);

    /// <summary>Shape the typed error response from a message + elapsed milliseconds. Subclasses
    /// typically return <c>new() { Success = false, ErrorMessage = message, LatencyMs = (int)elapsedMs }</c>.</summary>
    protected abstract TResponse BuildErrorResponse(string errorMessage, long elapsedMs);

    public async Task<TResponse> HandleAsync(TRequest request, RpcContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        AiSettings settings;
        try
        {
            settings = Services.SettingsProvider();
            CheckPrivacyConsent(settings);
        }
        catch (PrivacyConsentRequiredException consentEx)
        {
            sw.Stop();
            Log.Information("{Handler}: privacy consent required", GetType().Name);
            return BuildErrorResponse(consentEx.Message, sw.ElapsedMilliseconds);
        }

        try
        {
            return await InvokeAsync(request, ctx, settings, sw, ct).ConfigureAwait(false);
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

    // ───────── Privacy-consent gate (lifted from AiRequestHandler.CheckPrivacyConsent) ─────────

    private static readonly HashSet<string> LocalProviders =
        new(StringComparer.OrdinalIgnoreCase) { "ollama", "lmstudio" };

    private static void CheckPrivacyConsent(AiSettings settings)
    {
        if (!settings.PrivacyConsentRequired) return;
        var provider = settings.Provider?.Trim() ?? string.Empty;
        if (LocalProviders.Contains(provider)) return;
        var providerDisplay = string.IsNullOrEmpty(provider) ? "your AI provider" : provider;
        throw new PrivacyConsentRequiredException(
            $"CONSENT_REQUIRED:Data will be sent to {providerDisplay}. Please confirm in settings.");
    }
}
