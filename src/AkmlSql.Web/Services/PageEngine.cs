using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AkmlSql.Web.Services;

public enum PageEngineKind { Engine, Static, Unknown }

/// <summary>Spec 047 (WEB-001): how the page's engine was found.</summary>
public enum PageEngineSource
{
    /// <summary>The engine serves this page itself (standalone hosting; <c>HEAD /</c> answers <c>X-AKML-Host: engine</c>).</summary>
    ServedPage,

    /// <summary>A static host (IIS) serves the page, and setup published the engine's port next to it (<c>akml-engine.json</c>).</summary>
    InstallManifest,
}

public sealed record PageEngineInfo(string Host, int Port, string Scheme);

public sealed record PageEngineResult(PageEngineKind Kind, PageEngineInfo? Engine = null)
{
    /// <summary>Spec 047: where <see cref="Engine"/> came from; <see cref="PageEngineSource.ServedPage"/> unless a manifest named it.</summary>
    public PageEngineSource Source { get; init; } = PageEngineSource.ServedPage;
}

/// <summary>
/// Spec 047 (WEB-001, contracts/engine-pairing.md §1): what <c>web-engine-manifest.ps1</c> writes next to the bundle of a page a
/// static host serves. Host is always the page's own; the file never carries a host, PIN, token or path.
/// </summary>
public sealed class PageEngineManifest
{
    public int ManifestVersion { get; set; }
    public int BridgePort { get; set; }
    public string? Scheme { get; set; }
}

public interface IPageEngine
{
    Task<PageEngineResult> GetAsync(CancellationToken ct = default);
    PageEngineInfo? PairingPrefill { get; set; }
}

/// <summary>
/// Session-only detection of the engine that served this page, or (spec 047) of the engine the page's installer declared
/// next to a static bundle.
/// </summary>
internal sealed class PageEngine : IPageEngine
{
    private readonly IJSRuntime _js;
    private readonly Uri _pageBase;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1);
    private PageEngineResult? _cached;
    private DateTimeOffset? _firstFailure;
    private int _failures;

    public PageEngine(IJSRuntime js, NavigationManager navigation, Func<DateTimeOffset>? clock = null)
    {
        _js = js;
        _pageBase = new Uri(navigation.BaseUri);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public PageEngineInfo? PairingPrefill { get; set; }

    public async Task<PageEngineResult> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached != null) return _cached;
            if (_firstFailure is { } first && _clock() - first >= TimeSpan.FromSeconds(60))
                return _cached = new(PageEngineKind.Static);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await using var module = await _js.InvokeAsync<IJSObjectReference>("import", timeout.Token, "./js/akml-page-engine.js");
                var engine = await module.InvokeAsync<bool>("detect", timeout.Token, new Uri(_pageBase, "/").AbsoluteUri);
                if (engine) return _cached = new(PageEngineKind.Engine, EngineEndpoint.FromPage(_pageBase));
                // Spec 047 (WEB-001): a static page may still have an engine on this machine; setup says which port.
                return _cached = await ReadManifestAsync(module, ct) ?? new(PageEngineKind.Static);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is JSException or OperationCanceledException)
            {
                _firstFailure ??= _clock();
                _failures++;
                if (_failures >= 3 || _clock() - _firstFailure.Value >= TimeSpan.FromSeconds(60))
                    return _cached = new(PageEngineKind.Static);
                return new(PageEngineKind.Unknown);
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Spec 047 (WEB-001, contracts/engine-pairing.md §2): the engine <c>akml-engine.json</c> declares, on this page's host. Null
    /// when the file is missing, unreadable, from a newer installer (<c>manifestVersion</c> other than 1), or names a port
    /// outside 1024–65535 or a scheme other than ws / wss — the page is then static, as before.
    /// </summary>
    private async Task<PageEngineResult?> ReadManifestAsync(IJSObjectReference module, CancellationToken ct)
    {
        PageEngineManifest? manifest;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            manifest = await module.InvokeAsync<PageEngineManifest?>("readManifest", timeout.Token,
                new Uri(_pageBase, "/akml-engine.json").AbsoluteUri);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is JSException or OperationCanceledException)
        {
            return null;
        }
        if (manifest is not { ManifestVersion: 1, BridgePort: >= 1024 and <= 65535 } || manifest.Scheme is not ("ws" or "wss"))
            return null;
        return new(PageEngineKind.Engine, new PageEngineInfo(_pageBase.Host, manifest.BridgePort, manifest.Scheme))
        {
            Source = PageEngineSource.InstallManifest,
        };
    }
}
