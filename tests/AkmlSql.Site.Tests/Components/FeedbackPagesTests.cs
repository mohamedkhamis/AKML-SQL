using AkmlSql.Site.Analytics;
using AkmlSql.Site.Components.Layout;
using FeedbackPage = AkmlSql.Site.Components.Pages.Feedback;
using AkmlSql.Site.Components.Pages.Admin;
using AkmlSql.Site.Feedback;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>
/// The public feedback form and the admin inbox that receives it.
/// <para>
/// The form must work without JavaScript and must keep what the visitor typed when it sends them
/// back to fix something; the inbox must show what a stranger wrote as text, never as markup.
/// </para>
/// </summary>
public sealed class FeedbackPagesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _dir = new();
    private readonly FeedbackStore _store;

    public FeedbackPagesTests()
    {
        _store = new FeedbackStore(Path.Combine(_dir.Path, "analytics.db"));
    }

    public void Dispose()
    {
        _store.Dispose();
        _dir.Dispose();
    }

    private BunitContext NewCtx(string path = "/feedback", Action<FeedbackOptions>? email = null)
    {
        var ctx = new BunitContext();
        ctx.Services.AddAntiforgery();
        ctx.Services.AddSingleton(_store);
        ctx.Services.AddSingleton<FeedbackRateLimit>();
        ctx.Services.AddSingleton<FeedbackEmailStatus>();
        ctx.Services.AddSingleton(new ReportClock(TimeZoneInfo.Utc));
        ctx.Services.AddSingleton<IFeedbackMailer, NullMailer>();
        ctx.Services.AddSingleton<ILogger<FeedbackNotifier>>(NullLogger<FeedbackNotifier>.Instance);
        ctx.Services.Configure<FeedbackOptions>(o => email?.Invoke(o));
        ctx.Services.AddSingleton<FeedbackNotifier>();
        ctx.Services.AddSingleton(new GeoLookup(Path.Combine(_dir.Path, "no-such-geo.mmdb")));

        var nav = ctx.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(nav.BaseUri.TrimEnd('/') + path);
        return ctx;
    }

    private string CurrentUri(BunitContext ctx) => ctx.Services.GetRequiredService<NavigationManager>().Uri;

    // --- the public form ---------------------------------------------------------------------

    [Fact]
    public void TheForm_OffersEveryCategory_AndMakesEmailOptional()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<FeedbackPage>();

        var radios = cut.FindAll("input[type='radio'][name='Form.Category']");
        Assert.Equal(FeedbackCategory.All.Select(c => c.Key), radios.Select(r => r.GetAttribute("value")));
        Assert.DoesNotContain(radios, r => r.HasAttribute("checked"));

        Assert.NotNull(cut.Find("textarea#feedback-message"));
        var email = cut.Find("input#feedback-email");
        Assert.Equal("email", email.GetAttribute("type"));
        Assert.False(email.HasAttribute("required"));
        Assert.Contains("(optional)", cut.Find("label[for='feedback-email']").TextContent, StringComparison.Ordinal);

        // Every input has a visible label (the honeypot's is inside the hidden trap).
        Assert.NotNull(cut.Find("label[for='feedback-message']"));
        Assert.NotNull(cut.Find("a[href='/privacy#feedback']"));
    }

    [Fact]
    public void TheSpamTrap_IsHiddenFromPeopleAndScreenReaders()
    {
        using var ctx = NewCtx();

        var cut = ctx.Render<FeedbackPage>();

        var trap = cut.Find(".feedback-trap");
        Assert.Equal("true", trap.GetAttribute("aria-hidden"));
        Assert.Equal("-1", trap.QuerySelector("input")!.GetAttribute("tabindex"));
    }

    [Fact]
    public void ALinkCanPreselectTheCategory_AndRecordWhereItCameFrom()
    {
        using var ctx = NewCtx("/feedback?from=/download&about=download");

        var cut = ctx.Render<FeedbackPage>();

        Assert.True(cut.Find("input[type='radio'][value='download']").HasAttribute("checked"));
        Assert.Equal("/download", cut.Find("input[type='hidden'][name='Form.Page']").GetAttribute("value"));
    }

    [Theory]
    [InlineData("/feedback?from=https://evil.example/&about=nonsense")]
    [InlineData("/feedback?from=//evil.example&about=")]
    public void AForeignPageOrUnknownCategory_IsIgnored(string path)
    {
        using var ctx = NewCtx(path);

        var cut = ctx.Render<FeedbackPage>();

        Assert.DoesNotContain(cut.FindAll("input[type='radio']"), r => r.HasAttribute("checked"));
        Assert.True(string.IsNullOrEmpty(cut.Find("input[type='hidden'][name='Form.Page']").GetAttribute("value")));
    }

    [Fact]
    public void AfterSending_TheFormIsReplacedByAConfirmation()
    {
        using var ctx = NewCtx("/feedback?sent=1");

        var cut = ctx.Render<FeedbackPage>();

        Assert.Contains("your message was sent", cut.Find(".feedback-sent").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void AnIncompleteMessage_ComesBackWithTheProblemsNamed_AndNothingStored()
    {
        using var ctx = NewCtx();
        var cut = ctx.Render<FeedbackPage>();

        cut.Find("#feedback-email").Change("not-an-address");
        cut.Find("form").Submit();

        var errors = cut.Find(".feedback-errors[role='alert']").TextContent;
        Assert.Contains("Choose what the message is about.", errors, StringComparison.Ordinal);
        Assert.Contains("Describe the problem", errors, StringComparison.Ordinal);
        Assert.Contains("email address", errors, StringComparison.Ordinal);
        // What they typed is still there to fix.
        Assert.Equal("not-an-address", cut.Find("#feedback-email").GetAttribute("value"));
        Assert.Empty(_store.List(handled: null));
    }

    [Fact]
    public void AValidMessage_IsStored_AndTheVisitorIsRedirected()
    {
        using var ctx = NewCtx("/feedback?from=/download&about=download");
        var cut = ctx.Render<FeedbackPage>();

        cut.Find("#feedback-message").Change("  The installer stops at 50%.  ");
        cut.Find("#feedback-email").Change("visitor@example.com");
        cut.Find("form").Submit();

        var item = Assert.Single(_store.List(handled: false));
        Assert.Equal(FeedbackCategory.Download, item.Category);
        Assert.Equal("The installer stops at 50%.", item.Message);
        Assert.Equal("visitor@example.com", item.Email);
        Assert.Equal("/download", item.Page);
        Assert.EndsWith("/feedback?sent=1", CurrentUri(ctx), StringComparison.Ordinal);
    }

    [Fact]
    public void AFilledSpamTrap_LooksLikeSuccess_ButStoresNothing()
    {
        using var ctx = NewCtx("/feedback?about=problem");
        var cut = ctx.Render<FeedbackPage>();

        cut.Find("#feedback-message").Change("Buy cheap things at spam.example");
        cut.Find("#feedback-website").Change("https://spam.example");
        cut.Find("form").Submit();

        Assert.Empty(_store.List(handled: null));
        Assert.EndsWith("/feedback?sent=1", CurrentUri(ctx), StringComparison.Ordinal);
    }

    // --- the inbox ---------------------------------------------------------------------------

    private FeedbackItem Add(string message, string? email = null, bool handled = false)
    {
        var item = _store.Add(FeedbackCategory.Complaint, message, email, "/download", "Egypt", "Chrome", Now);
        if (handled)
        {
            _store.SetHandled(item.Id, handled: true, Now.AddHours(1));
        }

        return item;
    }

    [Fact]
    public void TheInbox_ShowsOpenMessagesWithTabCounts()
    {
        Add("first open message");
        Add("second open message");
        Add("an old handled message", handled: true);
        using var ctx = NewCtx("/admin/feedback");

        var cut = ctx.Render<AdminFeedback>();

        Assert.Equal(2, cut.FindAll(".feedback-item").Count);
        Assert.DoesNotContain("an old handled message", cut.Markup, StringComparison.Ordinal);
        var tabs = cut.FindAll(".admin-ranges a").Select(a => a.TextContent.Trim()).ToList();
        Assert.Equal(["Open (2)", "Handled (1)", "All (3)"], tabs);
        Assert.Equal("true", cut.Find(".admin-ranges a.is-current").GetAttribute("aria-current"));
    }

    [Fact]
    public void TheHandledTab_OffersReopen()
    {
        var item = Add("an old handled message", handled: true);
        using var ctx = NewCtx("/admin/feedback?show=handled");

        var cut = ctx.Render<AdminFeedback>();

        Assert.Single(cut.FindAll(".feedback-item.is-handled"));
        Assert.NotNull(cut.Find($"form[action='/admin/feedback/{item.Id}/reopen'][method='post']"));
        Assert.Equal("handled", cut.Find("input[name='show']").GetAttribute("value"));
    }

    [Fact]
    public void WhatAStrangerWrote_IsShownAsText()
    {
        Add("<script>alert('x')</script><img src=x onerror=alert(1)>");
        using var ctx = NewCtx("/admin/feedback");

        var cut = ctx.Render<AdminFeedback>();

        var message = cut.Find(".feedback-item-message");
        Assert.Empty(message.Children);
        Assert.Contains("<script>", message.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageWithAnAddress_CanBeRepliedTo()
    {
        var item = Add("Please call me back.", email: "visitor@example.com");
        Add("No address on this one.");
        using var ctx = NewCtx("/admin/feedback");

        var cut = ctx.Render<AdminFeedback>();

        var reply = cut.Find("a[href^='mailto:']");
        Assert.StartsWith($"mailto:visitor@example.com?subject=Re%3A%20your%20AKML%20SQL%20feedback%20%28%23{item.Id}%29",
            reply.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Contains("can't reply", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyInbox_SaysSo()
    {
        using var ctx = NewCtx("/admin/feedback");

        var cut = ctx.Render<AdminFeedback>();

        Assert.Contains("Nothing open", cut.Find(".admin-empty").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WithEmailOff_TheInboxExplainsHowToTurnItOn()
    {
        using var ctx = NewCtx("/admin/feedback");

        var cut = ctx.Render<AdminFeedback>();

        Assert.Contains("Feedback__NotifyEmail", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("scripts/set-feedback-email.ps1", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("form[action='/admin/feedback/test-email']"));
    }

    [Fact]
    public void WithEmailOn_TheInboxOffersATest_AndNeverShowsThePassword()
    {
        using var ctx = NewCtx("/admin/feedback", o =>
        {
            o.NotifyEmail = "owner@example.com";
            o.Smtp.Host = "smtp.example.com";
            o.Smtp.UserName = "sender@example.com";
            o.Smtp.Password = "super-secret-app-password";
        });

        var cut = ctx.Render<AdminFeedback>();

        Assert.Contains("owner@example.com", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("form[action='/admin/feedback/test-email'][method='post']"));
        Assert.DoesNotContain("super-secret-app-password", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedEmail_IsShownInTheInbox()
    {
        using var ctx = NewCtx("/admin/feedback", o =>
        {
            o.NotifyEmail = "owner@example.com";
            o.Smtp.Host = "smtp.example.com";
        });
        ctx.Services.GetRequiredService<FeedbackEmailStatus>().RecordFailure(Now, "5.7.8 Username and Password not accepted");

        var cut = ctx.Render<AdminFeedback>();

        Assert.Contains("5.7.8 Username and Password not accepted", cut.Find("[role='alert']").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("handled", "Marked handled.")]
    [InlineData("deleted", "Deleted")]
    [InlineData("missing", "no longer exists")]
    public void TheResultOfAnAction_IsConfirmed(string result, string expected)
    {
        using var ctx = NewCtx($"/admin/feedback?result={result}");

        var cut = ctx.Render<AdminFeedback>();

        Assert.Contains(expected, cut.Find(".notice[role='status']").TextContent, StringComparison.Ordinal);
    }

    // --- the portal badge --------------------------------------------------------------------

    [Fact]
    public void OpenMessages_AreCountedBesideFeedbackOnEveryPortalPage()
    {
        Add("one");
        Add("two");
        Add("done", handled: true);
        using var ctx = NewCtx("/admin");

        var cut = ctx.Render<AdminLayout>();

        var link = cut.Find("a[href^='/admin/feedback']");
        Assert.Equal("2", link.QuerySelector(".admin-badge")!.TextContent.Trim());
        // The number alone means nothing to a screen reader; the hidden text completes it.
        Assert.Contains("open messages", link.QuerySelector(".visually-hidden")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOpenMessages_NoBadge()
    {
        Add("done", handled: true);
        using var ctx = NewCtx("/admin");

        var cut = ctx.Render<AdminLayout>();

        Assert.Empty(cut.FindAll(".admin-badge"));
    }

    private sealed class NullMailer : IFeedbackMailer
    {
        public Task SendAsync(System.Net.Mail.MailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
