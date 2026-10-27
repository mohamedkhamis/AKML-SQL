using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace AkmlSql.Web.Services;

public enum PageEngineKind { Engine, Static, Unknown }
public sealed record PageEngineInfo(string Host, int Port, string Scheme);
public sealed record PageEngineResult(PageEngineKind Kind, PageEngineInfo? Engine = null);

public interface IPageEngine
{
    Task<PageEngineResult> GetAsync(CancellationToken ct = default);
    PageEngineInfo? PairingPrefill { get; set; }
}

/// <summary>Session-only detection of the engine that served this page.</summary>
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
                return _cached = engine ? new(PageEngineKind.Engine, EngineEndpoint.FromPage(_pageBase)) : new(PageEngineKind.Static);
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

}
