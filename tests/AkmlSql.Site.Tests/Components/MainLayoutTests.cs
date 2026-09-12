using AkmlSql.Site.Components.Layout;
using AkmlSql.Site.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// Spec 034 T028/T032 (US3 + accessibility): bUnit tests for the shell layout — theme
/// toggle control in the header (progressive enhancement, inert without JS), skip link
/// targeting the main landmark, and aria labels on the nav landmarks.
/// </summary>
public sealed class MainLayoutTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    /// <summary>
    /// Spec 038 (US5): MainLayout now renders the consent bar, which needs antiforgery and the
    /// settings store (for the retention figure it quotes). Registered here so these layout tests
    /// keep exercising the real composed layout rather than a stripped-down one.
    /// </summary>
    private BunitContext NewCtx()
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();

        var settings = new SiteSettingsStore(Path.Combine(_dir.Path, "settings.db"));
        settings.CreateTableIfMissing();
        settings.Load();
        ctx.Services.AddSingleton(settings);

        return ctx;
    }

    [Fact]
    public void Header_RendersThemeToggleButton_WithAccessibleLabel()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<MainLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var toggle = cut.Find("button#theme-toggle");
        Assert.Equal("button", toggle.GetAttribute("type"));
        Assert.False(string.IsNullOrWhiteSpace(toggle.GetAttribute("aria-label")));
        // Both icons are present; CSS swaps visibility from data-akml-theme (no JS needed).
        Assert.NotNull(toggle.QuerySelector(".theme-toggle-moon"));
        Assert.NotNull(toggle.QuerySelector(".theme-toggle-sun"));
    }

    [Fact]
    public void SkipLink_TargetsMainContentLandmark()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<MainLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var skip = cut.Find("a.skip-link");
        Assert.Equal("#main-content", skip.GetAttribute("href"));
        Assert.NotNull(cut.Find("main#main-content"));
    }

    [Fact]
    public void HeaderNav_HasAriaLabel_AndAllPrimaryLinks()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<MainLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var nav = cut.Find("header nav");
        Assert.False(string.IsNullOrWhiteSpace(nav.GetAttribute("aria-label")));
        Assert.NotNull(cut.Find("a[href='/']"));
        Assert.NotNull(cut.Find("a[href='/features']"));
        Assert.NotNull(cut.Find("a[href='/docs']"));
        Assert.NotNull(cut.Find("a[href='/download']"));
    }

    [Fact]
    public void MobileNav_RendersCssOnlyCheckboxToggle_InsideLabel()
    {
        // Site redesign: the <=768px menu is a checkbox inside its label — no JS required.
        // The input is the real, keyboard-focusable control; CSS (:has(:checked)) opens the
        // panel and swaps the open/close icons.
        using var ctx = NewCtx();

        var cut = ctx.Render<MainLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var toggle = cut.Find("label.nav-toggle-btn input.nav-toggle[type='checkbox']");
        Assert.False(string.IsNullOrWhiteSpace(toggle.GetAttribute("aria-label")));
        Assert.NotNull(cut.Find("label.nav-toggle-btn .nav-icon-open"));
        Assert.NotNull(cut.Find("label.nav-toggle-btn .nav-icon-close"));
    }

    [Fact]
    public void Footer_RendersLinkColumns_WithHeadings()
    {
        // Site redesign: multi-column footer (Product / Docs / Legal & source) with the
        // Support slot remaining a reserved comment (FR-010).
        using var ctx = NewCtx();

        var cut = ctx.Render<MainLayout>(ps => ps.Add(p => p.Body, "<p>body</p>"));

        var columns = cut.FindAll(".site-footer-col");
        Assert.Equal(3, columns.Count);
        Assert.Contains(cut.FindAll(".site-footer-heading"), h => h.TextContent.Trim() == "Product");
        Assert.Contains(cut.FindAll(".site-footer-heading"), h => h.TextContent.Trim() == "Docs");
        Assert.Contains(cut.FindAll(".site-footer-heading"), h => h.TextContent.Trim() == "Legal & source");
        // Docs column links real documents; Legal column keeps the FR-011 repo/license links.
        // DOC-001: architecture.md is no longer published, so the column leads with the guide a
        // first-time visitor actually wants. FooterDocLinksTests checks every slug here resolves.
        Assert.NotNull(cut.Find(".site-footer a[href='/docs/topics/getting-started']"));
        Assert.NotNull(cut.Find(".site-footer a[href='https://github.com/mohamedkhamis/AKML-SQL/blob/master/LICENSE.txt']"));
    }
}
