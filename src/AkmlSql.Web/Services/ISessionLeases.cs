using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AkmlSql.Web.Services;

/// <summary>The answer to a lease claim.</summary>
public enum LeaseClaim
{
    /// <summary>This window holds the lease now (or already did).</summary>
    Held,

    /// <summary>Another live window of the app holds it.</summary>
    Refused,

    /// <summary>This browser cannot tell: the page then shares no stored session id with any other window.</summary>
    Unavailable,
}

/// <summary>
/// Spec 043 (review of US1) — a window's lease on an engine session id. Query tabs are stored per browser profile, so two
/// windows of the app read the same engine session ids; without a lease the second window's reconnect would release the
/// first window's live session (rolling back its open transaction) and both would then run on one engine connection.
/// A window holds the lease of every tab session it uses for as long as the page lives; the browser drops a page's leases when
/// it unloads, so a reload takes its tabs' sessions back (R2) while another live window never can.
/// <para>
/// A window that may not use a tab's stored session (another window holds it, or the browser cannot tell) runs the tab on a
/// session of its own and remembers it for this browser tab only (<see cref="WindowSessionAsync"/>), so its own reload takes
/// that session back too.
/// </para>
/// </summary>
public interface ISessionLeases
{
    /// <summary>Claim the lease on <paramref name="sessionId"/>. <paramref name="fresh"/>: an id this window has just made, which
    /// no other window can hold, so nothing needs to be asked.</summary>
    Task<LeaseClaim> ClaimAsync(string sessionId, bool fresh = false);

    /// <summary>Give a lease up (the tab was closed or the editor session reset).</summary>
    Task ReleaseAsync(string sessionId);

    /// <summary>The engine session this window (this browser tab, across its reloads) used for query tab
    /// <paramref name="tabId"/> because it could not use the stored one; null when there is none.</summary>
    Task<string?> WindowSessionAsync(string tabId) => Task.FromResult<string?>(null);

    /// <summary>Remember the engine session this window uses for query tab <paramref name="tabId"/>.</summary>
    Task RememberWindowSessionAsync(string tabId, string sessionId) => Task.CompletedTask;

    /// <summary>Forget it (the tab was closed, or this window uses the stored session again).</summary>
    Task ForgetWindowSessionAsync(string tabId) => Task.CompletedTask;
}

/// <summary>Every claim is held: one window only (unit tests).</summary>
internal sealed class NoSessionLeases : ISessionLeases
{
    public static NoSessionLeases Instance { get; } = new();

    public Task<LeaseClaim> ClaimAsync(string sessionId, bool fresh = false) => Task.FromResult(LeaseClaim.Held);

    public Task ReleaseAsync(string sessionId) => Task.CompletedTask;
}

/// <summary>The Web Locks API, or a BroadcastChannel where a page has none (<c>wwwroot/js/akml-session-leases.js</c>).</summary>
internal sealed class JsSessionLeases : ISessionLeases, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public JsSessionLeases(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/akml-session-leases.js").ConfigureAwait(false);

    public async Task<LeaseClaim> ClaimAsync(string sessionId, bool fresh = false)
    {
        if (string.IsNullOrEmpty(sessionId)) return LeaseClaim.Held;
        try
        {
            var answer = await (await ModuleAsync().ConfigureAwait(false)).InvokeAsync<string>("claimSession", sessionId, fresh).ConfigureAwait(false);
            return answer switch
            {
                "held" => LeaseClaim.Held,
                "refused" => LeaseClaim.Refused,
                _ => LeaseClaim.Unavailable,
            };
        }
        // No JS (prerender) or no module: this window cannot tell, so it shares no stored id.
        catch (JSException) { return LeaseClaim.Unavailable; }
        catch (InvalidOperationException) { return LeaseClaim.Unavailable; }
        catch (JSDisconnectedException) { return LeaseClaim.Unavailable; }
    }

    public async Task ReleaseAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || _module == null) return;
        try { await _module.InvokeVoidAsync("releaseSession", sessionId).ConfigureAwait(false); }
        catch (JSException) { }
        catch (InvalidOperationException) { }
        catch (JSDisconnectedException) { }
    }

    public async Task<string?> WindowSessionAsync(string tabId)
    {
        if (string.IsNullOrEmpty(tabId)) return null;
        try { return await (await ModuleAsync().ConfigureAwait(false)).InvokeAsync<string?>("windowSession", tabId).ConfigureAwait(false); }
        catch (JSException) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (JSDisconnectedException) { return null; }
    }

    public Task RememberWindowSessionAsync(string tabId, string sessionId) => CallAsync("rememberWindowSession", tabId, sessionId);

    public Task ForgetWindowSessionAsync(string tabId) => CallAsync("forgetWindowSession", tabId);

    private async Task CallAsync(string name, params object?[] args)
    {
        try { await (await ModuleAsync().ConfigureAwait(false)).InvokeVoidAsync(name, args).ConfigureAwait(false); }
        catch (JSException) { }
        catch (InvalidOperationException) { }
        catch (JSDisconnectedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module == null) return;
        try { await _module.DisposeAsync().ConfigureAwait(false); }
        catch (JSDisconnectedException) { }
    }
}
