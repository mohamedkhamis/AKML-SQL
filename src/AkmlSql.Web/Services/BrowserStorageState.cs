using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 047 (WEB-003, research R6, contracts/connections-and-storage.md §4): whether the browser keeps this site's data —
/// saved connections, engine pairings, settings — until the user clears it, or may evict it when space runs low. The web
/// edition asks once (<c>navigator.storage.persist()</c>), the first time a connection or an engine is saved, and
/// Settings › Diagnostics shows the answer.
/// </summary>
public interface IBrowserStorageState
{
    /// <summary>
    /// Ask the browser to keep the data. The first answer is kept for the rest of the run; <paramref name="force"/> asks
    /// again (Diagnostics' Ask again). Null when the browser has no such API or the call failed.
    /// </summary>
    Task<bool?> RequestPersistenceAsync(bool force = false);

    /// <summary>Whether the data is kept until cleared (<c>navigator.storage.persisted()</c>); null when not reported.</summary>
    Task<bool?> IsPersistedAsync();
}

internal sealed class BrowserStorageState : IBrowserStorageState
{
    private readonly IJSRuntime _js;
    private Task<IJSObjectReference>? _module;
    private Task<bool?>? _requested;

    public BrowserStorageState(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    public Task<bool?> RequestPersistenceAsync(bool force = false)
    {
        if (force || _requested == null) _requested = CallAsync("persist");
        return _requested;
    }

    public Task<bool?> IsPersistedAsync() => CallAsync("persisted");

    private async Task<bool?> CallAsync(string function)
    {
        try
        {
            _module ??= _js.InvokeAsync<IJSObjectReference>("import", "./js/akml-indexeddb.js").AsTask();
            var module = await _module.ConfigureAwait(false);
            return await module.InvokeAsync<bool?>(function).ConfigureAwait(false);
        }
        catch (JSException) { return null; }
        catch (JSDisconnectedException) { return null; }
        catch (InvalidOperationException) { return null; }   // prerendering / no JS runtime
    }
}

/// <summary>
/// Spec 047 (WEB-003): the stores' fire-and-forget call after a successful add. A failure is swallowed: the save already
/// happened, and persistence is a request the browser may refuse.
/// </summary>
internal static class BrowserStorageRequest
{
    public static void Ask(IBrowserStorageState? storage)
    {
        if (storage == null) return;
        try
        {
            var task = storage.RequestPersistenceAsync();
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception)
        {
            // Never fails the save.
        }
    }
}
