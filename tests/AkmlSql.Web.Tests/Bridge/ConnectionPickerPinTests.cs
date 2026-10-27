using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Pairing with an engine on this computer. The report that prompted this: AKML SQL Web installed for
/// network (LAN) use, opened on the engine's own machine. Such an engine wants a PIN from every browser,
/// but the form never showed a PIN field for <c>localhost</c>, so the handshake was always refused with
/// <c>PinRequired</c> and there was nothing the user could do on the page.
/// </summary>
public sealed class ConnectionPickerPinTests : BunitContext
{
    [Fact]
    public void Add_starts_with_the_page_engines_host_and_port()
    {
        Services.AddSingleton<IPageEngine>(new FakePageEngine
            { Result = new(PageEngineKind.Engine, new("127.0.0.1", 59417, "ws")) });
        var cut = Render<ConnectionPickerComponent>();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Add").Click();
        Assert.Equal("127.0.0.1", cut.Find("[data-testid=engine-add-host]").GetAttribute("value"));
        Assert.Equal("59417", cut.Find("[data-testid=engine-add-port]").GetAttribute("value"));
    }

    [Fact]
    public async Task A_page_engine_PIN_prefill_opens_pairing_and_keeps_HTTPS_when_saved()
    {
        var info = new PageEngineInfo("localhost", 59417, "wss");
        Services.AddSingleton<IPageEngine>(new FakePageEngine { Result = new(PageEngineKind.Engine, info), PairingPrefill = info });
        _bridge.Answer = (_, _) => new() { Status = HandshakeStatus.Ok, NewBearerToken = "minted-token" };
        var cut = Render<ConnectionPickerComponent>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=engine-add-pin]")));
        Assert.Equal("localhost", cut.Find("[data-testid=engine-add-host]").GetAttribute("value"));
        cut.Find("[data-testid=engine-add-pin]").Change(Pin);
        cut.Find("[data-testid=engine-add-pair]").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=engine-add-pair]")));
        var saved = Assert.Single(await _store.ListAsync());
        Assert.True(saved.StrictScheme);
        Assert.Equal("wss", saved.ResolvedScheme);
    }

    private const string Pin = "948201";
    private readonly FakeEngineBridge _bridge = new();
    private readonly IConnectionStore _store = new ConnectionStore(new InMemoryIndexedDbAdapter());
    private readonly FakePairingTokenVault _vault = new();

    public ConnectionPickerPinTests()
    {
        Services.AddSingleton<IEngineBridge>(_bridge);
        Services.AddSingleton(_store);
        Services.AddSingleton<IPairingTokenVault>(_vault);
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
    }

    private IRenderedComponent<ConnectionPickerComponent> OpenAddForm(string host, int port)
    {
        var cut = Render<ConnectionPickerComponent>();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Add").Click();
        cut.Find("[data-testid='engine-add-host']").Input(host);
        cut.Find("[data-testid='engine-add-port']").Change(port.ToString());
        return cut;
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public async Task A_network_mode_engine_on_this_computer_asks_for_the_PIN_and_then_pairs(string host)
    {
        // The engine answers like a LAN-mode engine: no PIN, no entry; the right PIN mints a token.
        _bridge.Answer = (_, pin) => pin switch
        {
            null => new HandshakeResponse { Status = HandshakeStatus.PinRequired, ErrorMessage = "LAN mode requires a pairing PIN or bearer token." },
            Pin => new HandshakeResponse { Status = HandshakeStatus.Ok, NewBearerToken = "token-1", EngineVersion = "1.26.1016.0000" },
            _ => new HandshakeResponse { Status = HandshakeStatus.PinInvalid, ErrorMessage = "Pairing PIN was wrong or expired." },
        };

        var cut = OpenAddForm(host, 59417);
        Assert.Empty(cut.FindAll("[data-testid='engine-add-pin']")); // nothing in "localhost" says a PIN is needed

        cut.Find("[data-testid='engine-add-pair']").Click();

        // The engine said so: the PIN field appears, with why, instead of a dead end.
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='engine-add-pin']")));
        Assert.Contains("even from this computer", cut.Find("[data-testid='engine-add-pin-hint']").TextContent, StringComparison.Ordinal);
        Assert.Contains("pairing PIN", cut.Find("[data-testid='engine-add-error']").TextContent, StringComparison.Ordinal);
        Assert.Empty(await _store.ListAsync());

        cut.Find("[data-testid='engine-add-pin']").Change(Pin);
        cut.Find("[data-testid='engine-add-pair']").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='engine-add-pin']"))); // the form closed
        Assert.Equal(new string?[] { null, Pin }, _bridge.Handshakes.Select(h => h.PairingPin).ToArray());

        // Saved as a paired connection: the token goes with every reconnect, though the address is local.
        var saved = Assert.Single(await _store.ListAsync());
        Assert.Equal(host, saved.Host);
        Assert.Equal(59417, saved.Port);
        Assert.False(saved.IsLocalhost);
        Assert.NotNull(saved.BearerTokenWrappedRef);
        Assert.Equal("token-1", await _vault.RetrieveAsync(saved.Id));
        Assert.Equal(saved.Id, await _store.GetActiveIdAsync());
        Assert.Equal("Local", cut.Find(".akml-connections-badge").TextContent.Trim());
    }

    [Fact]
    public async Task A_localhost_mode_engine_still_pairs_without_a_PIN()
    {
        var cut = OpenAddForm("localhost", 47291);

        cut.Find("[data-testid='engine-add-pair']").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='engine-add-pair']")));
        var handshake = Assert.Single(_bridge.Handshakes);
        Assert.Null(handshake.PairingPin);
        var saved = Assert.Single(await _store.ListAsync());
        Assert.True(saved.IsLocalhost);
        Assert.Null(saved.BearerTokenWrappedRef);
    }

    [Fact]
    public void A_PIN_field_that_appeared_must_be_filled_before_the_next_try()
    {
        _bridge.Answer = (_, pin) => new HandshakeResponse { Status = pin is null ? HandshakeStatus.PinRequired : HandshakeStatus.Ok };
        var cut = OpenAddForm("localhost", 59417);
        cut.Find("[data-testid='engine-add-pair']").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='engine-add-pin']")));

        cut.Find("[data-testid='engine-add-pair']").Click(); // PIN still empty

        Assert.Equal("Enter the engine's pairing PIN.", cut.Find("[data-testid='engine-add-error']").TextContent.Trim());
        Assert.Single(_bridge.Handshakes); // nothing sent the second time
    }

    [Fact]
    public void A_wrong_PIN_is_reported_as_the_engine_said_it()
    {
        _bridge.Answer = (_, pin) => pin is null
            ? new HandshakeResponse { Status = HandshakeStatus.PinRequired }
            : new HandshakeResponse { Status = HandshakeStatus.PinInvalid, ErrorMessage = "Pairing PIN was wrong or expired." };
        var cut = OpenAddForm("localhost", 59417);
        cut.Find("[data-testid='engine-add-pair']").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='engine-add-pin']")));

        cut.Find("[data-testid='engine-add-pin']").Change("000000");
        cut.Find("[data-testid='engine-add-pair']").Click();

        cut.WaitForAssertion(() => Assert.Contains("wrong or expired", cut.Find("[data-testid='engine-add-error']").TextContent, StringComparison.Ordinal));
        Assert.Single(cut.FindAll("[data-testid='engine-add-pin']")); // still there for another try
    }
}
