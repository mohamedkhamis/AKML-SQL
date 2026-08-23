using System;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 025 follow-on (TLS fingerprint UI dialog — previously deferred §Out of Scope).
/// Banner is a non-blocking warning that surfaces a drift the bridge already auto-trusted
/// in-memory. The tests cover: absent-by-default (no drift ever fired), appears on drift,
/// shows redacted Last12 form for both old and new, dismiss clears it, second drift
/// queues behind the first.
/// </summary>
public sealed class TlsFingerprintMismatchBannerTests : BunitContext
{
    private readonly FakeEngineBridge _bridge;

    public TlsFingerprintMismatchBannerTests()
    {
        _bridge = new FakeEngineBridge();
        Services.AddSingleton<IEngineBridge>(_bridge);
        Services.AddSingleton(_bridge);
    }

    [Fact]
    public void AbsentByDefault_NoEventEverFired()
    {
        var cut = Render<TlsFingerprintMismatchBanner>();
        Assert.Empty(cut.FindAll("[data-testid='tls-fingerprint-banner']"));
    }

    [Fact]
    public void AppearsOnFirstMismatchEvent_ShowsRedactedThumbprints()
    {
        var cut = Render<TlsFingerprintMismatchBanner>();
        // 40-hex-char thumbprints — same shape as real SHA-1 cert hashes.
        _bridge.FireMismatch(new TlsFingerprintMismatch(
            ConnectionName: "Office LAN engine",
            OldThumbprint: "abcdef1234567890abcdef1234567890abcdef12",
            NewThumbprint: "0011223344556677889900112233445566778899"));

        var banner = cut.Find("[data-testid='tls-fingerprint-banner']");
        Assert.Contains("Office LAN engine", banner.TextContent);
        // Redacted form: "…" + last 12 chars. The 40-hex strings here are
        // "abcdef1234567890abcdef1234567890abcdef12" (old) and
        // "0011223344556677889900112233445566778899" (new) — last 12 of each are
        // "7890abcdef12" and "445566778899" respectively.
        var newCode = cut.Find("[data-testid='tls-banner-new']");
        Assert.Equal("…445566778899", newCode.TextContent);
        var oldCode = cut.Find("[data-testid='tls-banner-old']");
        Assert.Equal("…7890abcdef12", oldCode.TextContent);
    }

    [Fact]
    public void DismissClearsTheBanner()
    {
        var cut = Render<TlsFingerprintMismatchBanner>();
        _bridge.FireMismatch(new TlsFingerprintMismatch(
            "conn-1",
            "old-thumb-x".PadRight(40, 'a'),
            "new-thumb-y".PadRight(40, 'b')));

        Assert.NotEmpty(cut.FindAll("[data-testid='tls-fingerprint-banner']"));
        cut.Find("[data-testid='tls-banner-dismiss']").Click();
        Assert.Empty(cut.FindAll("[data-testid='tls-fingerprint-banner']"));
    }

    [Fact]
    public void SecondMismatchQueuesBehindFirst_RevealsAfterDismiss()
    {
        var cut = Render<TlsFingerprintMismatchBanner>();

        _bridge.FireMismatch(new TlsFingerprintMismatch("first",
            "oldA".PadRight(40, '0'), "newA".PadRight(40, '1')));
        _bridge.FireMismatch(new TlsFingerprintMismatch("second",
            "oldB".PadRight(40, '2'), "newB".PadRight(40, '3')));

        // First drift visible; "(1 more pending)" hint present.
        Assert.Contains("first", cut.Markup);
        Assert.Contains("1 more pending", cut.Markup);

        cut.Find("[data-testid='tls-banner-dismiss']").Click();

        // Second drift now visible; pending count gone.
        Assert.Contains("second", cut.Markup);
        Assert.DoesNotContain("more pending", cut.Markup);

        cut.Find("[data-testid='tls-banner-dismiss']").Click();
        Assert.Empty(cut.FindAll("[data-testid='tls-fingerprint-banner']"));
    }

    [Fact]
    public void Last12HelperRedactsToTrailingTwelveCharacters()
    {
        // Pure helper assertion — keeps the redaction contract bound to the test surface
        // rather than asserting it indirectly through the rendered DOM only.
        Assert.Equal("<empty>", TlsFingerprintMismatchBanner.Last12(null));
        Assert.Equal("<empty>", TlsFingerprintMismatchBanner.Last12(""));
        Assert.Equal("short", TlsFingerprintMismatchBanner.Last12("short"));
        Assert.Equal("…789012345678", TlsFingerprintMismatchBanner.Last12("0123456789012345678"));
    }
}
