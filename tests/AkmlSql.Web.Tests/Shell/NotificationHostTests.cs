using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T018, research R60, FR-086) — the one notification area for transient status messages
/// ("Imported 12 settings", "Copied 4 rows"). Information leaves on its own; an error stays until the
/// user dismisses it. At most three show at once.
/// </summary>
public sealed class NotificationHostTests : BunitContext
{
    private readonly NotificationService _notes = new();

    public NotificationHostTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<INotificationService>(_notes);
    }

    [Fact]
    public void An_info_toast_is_a_polite_status_and_leaves_on_its_own()
    {
        var cut = Render<NotificationHost>();
        _notes.Notify("Copied 4 rows.", NotificationKind.Info, TimeSpan.FromMilliseconds(40));

        var toast = cut.WaitForElement("[data-testid=toast]");
        Assert.Equal("status", toast.GetAttribute("role"));
        Assert.Equal("polite", toast.GetAttribute("aria-live"));
        Assert.Contains("Copied 4 rows.", toast.TextContent);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=toast]")), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_success_toast_is_also_a_status()
    {
        var cut = Render<NotificationHost>();
        _notes.Notify("Saved.", NotificationKind.Success);

        var toast = cut.WaitForElement("[data-testid=toast]");
        Assert.Equal("status", toast.GetAttribute("role"));
        Assert.Equal("success", toast.GetAttribute("data-kind"));
    }

    [Fact]
    public async Task An_error_toast_is_an_alert_and_stays_until_dismissed()
    {
        var cut = Render<NotificationHost>();
        _notes.Notify("Copy failed.", NotificationKind.Error, TimeSpan.FromMilliseconds(20));

        var toast = cut.WaitForElement("[data-testid=toast]");
        Assert.Equal("alert", toast.GetAttribute("role"));

        await Task.Delay(150);
        Assert.Single(cut.FindAll("[data-testid=toast]"));

        cut.Find("[data-testid=toast-close]").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=toast]")));
    }

    [Fact]
    public void At_most_three_toasts_show_and_the_oldest_goes_first()
    {
        var cut = Render<NotificationHost>();
        for (int i = 1; i <= 4; i++) _notes.Notify($"note {i}", NotificationKind.Error);

        cut.WaitForAssertion(() =>
        {
            var toasts = cut.FindAll("[data-testid=toast]");
            Assert.Equal(3, toasts.Count);
            Assert.DoesNotContain(toasts, t => t.TextContent.Contains("note 1"));
            Assert.Contains(toasts, t => t.TextContent.Contains("note 4"));
        });
    }

    [Fact]
    public void Toasts_never_use_the_error_banner_test_id()
    {
        var cut = Render<NotificationHost>();
        _notes.Notify("x", NotificationKind.Error);
        cut.WaitForElement("[data-testid=toast]");

        Assert.Empty(cut.FindAll("[data-testid=error-banner]"));
    }

    [Fact]
    public void Empty_text_is_ignored()
    {
        var cut = Render<NotificationHost>();
        _notes.Notify("   ", NotificationKind.Info);

        Assert.Empty(cut.FindAll("[data-testid=toast]"));
        Assert.Empty(_notes.Visible);
    }
}
