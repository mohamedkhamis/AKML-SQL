namespace AkmlSql.Site.Consent;

/// <summary>
/// Resolves the visitor's consent state and identity once per request, before anything reads them
/// (spec 038 T045; contract consent-and-identity §2).
/// <para>
/// Registered ahead of <c>VisitTrackingMiddleware</c> so tracking sees a resolved state rather than
/// re-reading cookies, and so there is exactly one place that decides what the visitor's state is.
/// </para>
/// <para>
/// <b>This middleware must never consult <c>GeoLookup</c>.</b> Consent is requested of every visitor
/// regardless of country (FR-043a), which means a missing or stale geo database cannot affect
/// whether consent is asked for — and there is no code path where it could.
/// </para>
/// </summary>
public sealed class ConsentMiddleware
{
    /// <summary><see cref="HttpContext.Items"/> key holding the resolved <see cref="ConsentState"/>.</summary>
    public const string StateKey = "akml.consent.state";

    /// <summary><see cref="HttpContext.Items"/> key holding the persistent visitor id, when there is one.</summary>
    public const string VisitorIdKey = "akml.consent.visitorId";

    private readonly RequestDelegate _next;

    public ConsentMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            Resolve(context, DateTimeOffset.UtcNow);
        }
        catch
        {
            // A cookie problem must never fail a request. Falling through leaves the state Unknown,
            // which is the safe default: nothing identifiable is written.
            context.Items[StateKey] = ConsentState.Unknown;
            context.Items[VisitorIdKey] = null;
        }

        await _next(context);
    }

    /// <summary>
    /// Resolution logic, separated so it can be tested without a pipeline.
    /// <para>
    /// When consent is granted but the identity cookie is missing or malformed, a FRESH id is
    /// issued. That visitor becomes a new individual; they are deliberately not re-linked to their
    /// previous rows by address or user-agent, because reconstructing a link the visitor broke is
    /// exactly what FR-022b forbids.
    /// </para>
    /// </summary>
    public static void Resolve(HttpContext context, DateTimeOffset now)
    {
        var state = ConsentCookies.Read(context.Request);
        string? visitorId = null;

        if (state == ConsentState.Granted)
        {
            visitorId = ConsentCookies.ReadVisitorId(context.Request);
            if (visitorId is null)
            {
                visitorId = ConsentCookies.NewVisitorId();
                ConsentCookies.Grant(context.Response, now, visitorId);
            }
        }

        context.Items[StateKey] = state;
        context.Items[VisitorIdKey] = visitorId;
    }

    /// <summary>The resolved state for this request; <see cref="ConsentState.Unknown"/> when unresolved.</summary>
    public static ConsentState StateOf(HttpContext? context) =>
        context?.Items.TryGetValue(StateKey, out var value) == true && value is ConsentState state
            ? state
            : ConsentState.Unknown;

    /// <summary>The resolved visitor id for this request, or null when there is none.</summary>
    public static string? VisitorIdOf(HttpContext? context) =>
        context?.Items.TryGetValue(VisitorIdKey, out var value) == true ? value as string : null;
}
