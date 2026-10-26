using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

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
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1);
    private PageEngineResult? _cached;
    private DateTimeOffset? _firstFailure;
    private int _failures;

    public PageEngine(HttpClient http, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
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
            using var request = new HttpRequestMessage(HttpMethod.Head, new Uri(_http.BaseAddress!, "/"));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.SetBrowserRequestCache(BrowserRequestCache.NoCache);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var engine = response.Headers.TryGetValues("X-AKML-Host", out var values) &&
                    values.Any(value => value.Equals("engine", StringComparison.OrdinalIgnoreCase));
                return _cached = engine ? new(PageEngineKind.Engine, EngineEndpoint.FromPage(_http.BaseAddress!)) : new(PageEngineKind.Static);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException)
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
