using AkmlSql.Site.Components.Pages;
using AkmlSql.Site.Consent;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 038 T057 (US5): the published privacy notice.
/// <para>
/// <b>This is the drift gate.</b> A notice that quietly stops matching what the site does is worse
/// than no notice at all — it is a false statement made to every visitor. The retention figure is
/// read from the same setting the store enforces, so changing the setting and not the notice cannot
/// happen: the test below changes the setting and requires the page to follow (SC-013).
/// </para>
/// </summary>
public sealed class PrivacyPageTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private BunitContext NewCtx(int retentionDays = 365)
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();

        var settings = new SiteSettingsStore(Path.Combine(_dir.Path, Guid.NewGuid().ToString("N") + ".db"));
        settings.CreateTableIfMissing();
        settings.Load();
        settings.Save(settings.Current with { IdentifiableRetentionDays = retentionDays }, "test");
        ctx.Services.AddSingleton(settings);

        return ctx;
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void TheStatedRetentionFollowsTheConfiguredSetting(int days)
    {
        using var ctx = NewCtx(days);

        var cut = ctx.Render<Privacy>();

        // SC-013 / contract C7.3. If someone hard-codes this number later, this test fails.
        Assert.Contains($"{days} days", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ItStatesPlainlyThatTheFullAddressAndAnIdentifierAreStoredWithConsent()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Privacy>();

        // FR-039: the notice must describe what is ACTUALLY collected after the 2026-09-12
        // decisions -- not the anonymised model the site used to have.
        Assert.Contains("full IP address", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ConsentCookies.VisitorCookieName, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("recognise this browser", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItStatesThatDecliningChangesNothingAboutTheDownload()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Privacy>();

        // FR-044 is a promise to the visitor; the notice is where they read it.
        Assert.Contains("including downloads", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ItOffersWithdrawalAndDeletion_WithoutJavaScript()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Privacy>();

        // FR-045 / FR-040.
        Assert.NotNull(cut.Find("form[method='post'][action='/privacy/forget']"));
        Assert.NotNull(cut.Find("form[action='/privacy/forget'] button[type='submit']"));
        Assert.Empty(cut.FindAll("script"));
    }

    [Fact]
    public void ItListsEveryCookieTheSiteSets()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Privacy>();

        Assert.Contains(ConsentCookies.ConsentCookieName, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(ConsentCookies.VisitorCookieName, cut.Markup, StringComparison.Ordinal);
        Assert.Contains("akml.admin", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterDeletion_ItConfirmsWhatWasRemoved()
    {
        using var ctx = NewCtx();
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("forgotten", "3"));

        var cut = ctx.Render<Privacy>();

        Assert.Contains("3 records deleted", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AfterDeletionWithNothingStored_ItSaysSoWithoutImplyingAnError()
    {
        using var ctx = NewCtx();
        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.GetUriWithQueryParameter("forgotten", "0"));

        var cut = ctx.Render<Privacy>();

        // Contract C4.4: the same reassuring answer whether they were tracked or not.
        Assert.Contains("Nothing was stored about this browser", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ItIsPubliclyReachable_AndNotNoIndexed()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<Privacy>();

        // A privacy notice nobody can find is not a notice. Unlike the portal, this page must be
        // indexable and must carry no robots exclusion.
        Assert.DoesNotContain("noindex", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
