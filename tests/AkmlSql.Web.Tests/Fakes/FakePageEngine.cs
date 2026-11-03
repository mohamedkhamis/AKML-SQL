using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests.Bridge;

internal sealed class FakePageEngine : IPageEngine
{
    internal PageEngineResult Result { get; set; } = new(PageEngineKind.Static);
    internal int Calls { get; private set; }
    public PageEngineInfo? PairingPrefill { get; set; }
    public Task<PageEngineResult> GetAsync(CancellationToken ct = default)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}
