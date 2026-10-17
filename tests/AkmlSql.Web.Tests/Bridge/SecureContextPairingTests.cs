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
/// Review finding WEB-03: opened over plain HTTP from another computer, the page has no Web Crypto, so a pairing token cannot
/// be stored. The form says so with the site's HTTPS address, and refuses a PIN pairing BEFORE the handshake — otherwise
/// the engine spends its PIN on a token the browser then loses.
/// </summary>
public sealed class SecureContextPairingTests : BunitContext
{
    private sealed class InsecureCrypto : IWebCryptoWrapper
    {
        public Task<WrappedSecret> WrapAsync(string plaintext, string aadString) => throw new InvalidOperationException("no Web Crypto");
        public Task<string> UnwrapAsync(WrappedSecret wrapped) => throw new InvalidOperationException("no Web Crypto");
        public Task<string> Sha256HexAsync(string text) => throw new InvalidOperationException("no Web Crypto");
        public Task<bool> IsSecureStorageAvailableAsync() => Task.FromResult(false);
    }

    private readonly FakeEngineBridge _bridge = new();
    private readonly IConnectionStore _store = new ConnectionStore(new InMemoryIndexedDbAdapter());

    public SecureContextPairingTests()
    {
        Services.AddSingleton<IEngineBridge>(_bridge);
        Services.AddSingleton(_store);
        Services.AddSingleton<IPairingTokenVault>(new FakePairingTokenVault());
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
        Services.AddSingleton<IWebCryptoWrapper>(new InsecureCrypto());
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("http://sql-host/settings/connections");
    }

    private IRenderedComponent<ConnectionPickerComponent> OpenAddForm(string host)
    {
        var cut = Render<ConnectionPickerComponent>();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Add").Click();
        cut.Find("[data-testid='engine-add-host']").Input(host);
        cut.Find("[data-testid='engine-add-port']").Change("47291");
        return cut;
    }

    [Fact]
    public async Task A_PIN_pairing_is_refused_before_the_handshake_and_the_HTTPS_address_is_given()
    {
        var cut = OpenAddForm("engine-host");
        Assert.Equal("https://sql-host/", cut.Find("[data-testid='secure-context-https-link']").GetAttribute("href"));

        cut.Find("[data-testid='engine-add-pin']").Change("123456");
        cut.Find("[data-testid='engine-add-pair']").Click();

        Assert.Empty(_bridge.Handshakes);            // the PIN was not spent
        Assert.Equal(ConnectionPickerComponent.InsecurePairingMessage, cut.Find("[data-testid='engine-add-error']").TextContent.Trim());
        Assert.Empty(await _store.ListAsync());
    }

    [Fact]
    public void A_localhost_engine_that_needs_no_token_still_pairs()
    {
        var cut = OpenAddForm("localhost");
        Assert.Empty(cut.FindAll("[data-testid='secure-context-notice']"));

        cut.Find("[data-testid='engine-add-pair']").Click();

        Assert.Single(_bridge.Handshakes);
    }

    [Theory]
    [InlineData("http://sql-host/", "https://sql-host/")]
    [InlineData("http://sql-host:8080/", "https://sql-host/")]
    [InlineData("http://192.0.2.10/app/", "https://192.0.2.10/app/")]
    public void The_HTTPS_address_is_the_same_site_on_the_default_port(string page, string expected) =>
        Assert.Equal(expected, SecureContextNotice.HttpsAddressOf(page));
}
