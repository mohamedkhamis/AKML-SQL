using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared.Settings;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 047 (ENG-001, contracts/engine-pairing.md §6): Settings › Connections lists the browsers paired with the active
/// engine, each with a label, when it was paired and last used, and a Revoke that asks first. Nothing is pruned on its own.
/// </summary>
public sealed class PairedBrowsersSectionTests : BunitContext
{
    private const string CurrentId = "aaaaaaaa11111111aaaaaaaa11111111aaaaaaaa11111111aaaaaaaa11111111";
    private const string OtherId = "bbbbbbbb22222222bbbbbbbb22222222bbbbbbbb22222222bbbbbbbb22222222";

    private readonly FakePairingService _pairing = new();
    private readonly FakeDialogService _dialogs = new();

    public PairedBrowsersSectionTests()
    {
        Services.AddSingleton<IPairingService>(_pairing);
        Services.AddSingleton<IDialogService>(_dialogs);
        _pairing.List = new PairingListResponse
        {
            PairingUsed = true,
            Entries = new[]
            {
                new PairingEntry
                {
                    Id = OtherId, Label = "Firefox on Linux · https://sql-host",
                    MintedAt = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero),
                    LastUsedAt = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero),
                    ExpiresAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),   // expired
                },
                new PairingEntry
                {
                    Id = CurrentId, Label = "Edge on Windows · http://localhost:59416", IsCurrent = true,
                    MintedAt = new DateTimeOffset(2026, 11, 19, 8, 0, 0, TimeSpan.Zero),
                    LastUsedAt = new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero),
                    ExpiresAt = new DateTimeOffset(2027, 2, 17, 0, 0, 0, TimeSpan.Zero),
                },
            },
        };
    }

    [Fact]
    public void Each_paired_browser_shows_its_label_dates_and_whether_it_is_this_one()
    {
        var cut = Render<PairedBrowsersList>();

        var current = cut.WaitForElement("[data-testid=paired-browser-aaaaaaaa]");
        Assert.Contains("Edge on Windows · http://localhost:59416", current.TextContent);
        Assert.Contains("This browser", current.TextContent);
        Assert.Contains("Paired", current.TextContent);
        Assert.Contains("Last used", current.TextContent);
        var other = cut.Find("[data-testid=paired-browser-bbbbbbbb]");
        Assert.Contains("Expired", other.TextContent);
        Assert.DoesNotContain("This browser", other.TextContent);
    }

    [Fact]
    public void Revoking_another_browser_asks_first_then_revokes_and_refreshes()
    {
        var cut = Render<PairedBrowsersList>();
        cut.WaitForElement("[data-testid=paired-browser-revoke-bbbbbbbb]").Click();

        var ask = Assert.Single(_dialogs.Confirms);
        Assert.Equal("Revoke pairing", ask.Title);
        Assert.Equal("Firefox on Linux · https://sql-host will need the engine's PIN to connect again.", ask.Message);
        Assert.True(ask.Destructive);
        Assert.Equal(new[] { OtherId }, _pairing.Revoked);
        cut.WaitForAssertion(() => Assert.Equal(2, _pairing.ListCalls));
    }

    [Fact]
    public void Revoking_this_browser_warns_that_it_will_be_disconnected()
    {
        var cut = Render<PairedBrowsersList>();
        cut.WaitForElement("[data-testid=paired-browser-revoke-aaaaaaaa]").Click();

        var ask = Assert.Single(_dialogs.Confirms);
        Assert.Equal("This browser will be disconnected and will need the engine's PIN to connect again.", ask.Message);
    }

    [Fact]
    public void Cancelling_the_confirmation_revokes_nothing()
    {
        _dialogs.ConfirmAnswer = _ => false;
        var cut = Render<PairedBrowsersList>();
        cut.WaitForElement("[data-testid=paired-browser-revoke-bbbbbbbb]").Click();

        Assert.Empty(_pairing.Revoked);
    }

    [Fact]
    public void Nothing_is_shown_without_the_capability_or_when_the_engine_uses_no_pairing()
    {
        _pairing.Available = false;
        Assert.Empty(Render<PairedBrowsersList>().FindAll("[data-testid=paired-browsers]"));

        _pairing.Available = true;
        _pairing.List = new PairingListResponse { PairingUsed = false };
        var cut = Render<PairedBrowsersList>();
        cut.WaitForAssertion(() => Assert.True(_pairing.ListCalls >= 1));
        Assert.Empty(cut.FindAll("[data-testid=paired-browsers]"));
    }

    private sealed class FakePairingService : IPairingService
    {
        public bool Available { get; set; } = true;
        public PairingListResponse? List { get; set; }
        public int ListCalls { get; private set; }
        public List<string> Revoked { get; } = new();

        public Task<PairingListResponse?> ListAsync(CancellationToken ct)
        {
            ListCalls++;
            return Task.FromResult(List);
        }

        public Task<PairingRevokeResponse?> RevokeAsync(string id, CancellationToken ct)
        {
            Revoked.Add(id);
            return Task.FromResult<PairingRevokeResponse?>(new PairingRevokeResponse { Ok = true, ClosedConnections = 1 });
        }

        public event Action? AvailabilityChanged { add { } remove { } }
    }
}
