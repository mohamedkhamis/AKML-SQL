using AkmlSql.Site.Components;
using AkmlSql.Site.Consent;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T056 (US5): the consent request as rendered.
/// <para>
/// The assertions that matter most here are the <b>negative</b> ones. The owner chose a bar that
/// asks everyone and blocks nobody, accepting that it yields the smallest individuals list of any
/// option considered. The obvious-looking "improvements" — make it a modal, treat silence as
/// consent — are FR-043b and FR-043a violations, so the absence of modal machinery is pinned
/// explicitly rather than left to reviewer memory.
/// </para>
/// </summary>
public sealed class ConsentBarTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private BunitContext NewCtx(ConsentState state, int retentionDays = 365)
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();

        var settings = new SiteSettingsStore(Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".db"));
        settings.CreateTableIfMissing();
        settings.Load();
        settings.Save(settings.Current with { IdentifiableRetentionDays = retentionDays }, "test");
        ctx.Services.AddSingleton(settings);

        var http = new DefaultHttpContext();
        http.Request.Path = "/download";
        http.Items[ConsentMiddleware.StateKey] = state;
        ctx.Services.AddSingleton<HttpContext>(http);

        return ctx;
    }

    private static IRenderedComponent<ConsentBar> Render(BunitContext ctx)
    {
        var http = ctx.Services.GetRequiredService<HttpContext>();
        return ctx.Render<ConsentBar>(ps => ps.AddCascadingValue(http));
    }

    [Fact]
    public void ItRendersForAVisitorWhoHasNotAnswered()
    {
        using var ctx = NewCtx(ConsentState.Unknown);

        var cut = Render(ctx);

        Assert.NotNull(cut.Find(".consent-bar"));
        Assert.Equal("region", cut.Find(".consent-bar").GetAttribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(cut.Find(".consent-bar").GetAttribute("aria-label")));
    }

    [Theory]
    [InlineData(ConsentState.Granted)]
    [InlineData(ConsentState.Denied)]
    public void ItDoesNotRenderOnceTheVisitorHasAnswered(ConsentState state)
    {
        using var ctx = NewCtx(state);

        var cut = Render(ctx);

        // FR-046: someone who declined is never nagged again.
        Assert.Empty(cut.FindAll(".consent-bar"));
    }

    [Fact]
    public void AcceptAndDeclineAreBothOfferedAsSubmitButtons()
    {
        using var ctx = NewCtx(ConsentState.Unknown);

        var cut = Render(ctx);

        var accept = cut.Find("button[name='choice'][value='granted']");
        var decline = cut.Find("button[name='choice'][value='denied']");

        // FR-043b: equal prominence. Both are real buttons in the same form, same size class --
        // not a prominent Accept beside a link-styled "manage preferences".
        Assert.Equal("submit", accept.GetAttribute("type"));
        Assert.Equal("submit", decline.GetAttribute("type"));
        Assert.Contains("consent-btn", accept.GetAttribute("class"));
        Assert.Contains("consent-btn", decline.GetAttribute("class"));
    }

    [Fact]
    public void ItIsNotAModal_AndBlocksNothing()
    {
        using var ctx = NewCtx(ConsentState.Unknown);

        var cut = Render(ctx);

        // FR-043b / contract C2.5. Each of these would make the bar blocking; none may appear.
        Assert.Empty(cut.FindAll("[aria-modal]"));
        Assert.Empty(cut.FindAll("[role='dialog']"));
        Assert.Empty(cut.FindAll("dialog"));
        Assert.Empty(cut.FindAll(".modal, .overlay, .backdrop"));
        Assert.DoesNotContain("autofocus", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItWorksWithoutJavaScript()
    {
        using var ctx = NewCtx(ConsentState.Unknown);

        var cut = Render(ctx);

        var form = cut.Find("form[method='post'][action='/consent']");
        Assert.NotNull(form);
        Assert.NotNull(cut.Find("input[type='hidden'][name='returnUrl']"));
        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("onclick", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItReturnsTheVisitorToThePageTheyWereOn()
    {
        using var ctx = NewCtx(ConsentState.Unknown);

        var cut = Render(ctx);

        Assert.Equal("/download", cut.Find("input[name='returnUrl']").GetAttribute("value"));
    }

    [Fact]
    public void ItNamesCookiesAndTheAddress_AndLinksToTheFullNotice()
    {
        using var ctx = NewCtx(ConsentState.Unknown);

        var cut = Render(ctx);

        // Short-form banner: enough for the choice to be informed — cookies AND the IP address are
        // named — with the durations, the stored columns and the deletion route one click away.
        Assert.Contains("cookies", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IP address", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(cut.Find("a[href='/privacy']"));
    }

    [Fact]
    public void ItStaysShort_SoPeopleActuallyReadIt()
    {
        using var ctx = NewCtx(ConsentState.Unknown);

        var cut = Render(ctx);

        var text = cut.Find(".consent-bar-text").TextContent.Trim();
        // Split on any whitespace run; the null overload avoids a char-array literal.
        var words = text.Split((string[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        // The first version ran to two sentences and ~45 words, which is a paragraph, not a banner.
        Assert.True(words <= 30, $"Consent banner is {words} words: \"{text}\"");
    }

    [Fact]
    public void ItCarriesAnAntiforgeryToken()
    {
        using var ctx = NewCtx(ConsentState.Unknown);

        var cut = Render(ctx);

        Assert.NotNull(cut.Find("input[type='hidden'][name='__RequestVerificationToken']"));
    }
}
