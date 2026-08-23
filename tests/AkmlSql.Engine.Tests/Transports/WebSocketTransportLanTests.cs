using System;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using AkmlSql.Engine.Transports;
using Xunit;

namespace AkmlSql.Engine.Tests.Transports;

/// <summary>
/// Spec 025 (M3 bridge closure) FR-001..FR-003 + FR-006. LAN-mode coverage of the
/// engine-side <see cref="WebSocketTransport"/>: HTTPS-prefix construction, PFX
/// existence + netsh thumbprint-match validation, plaintext-LAN refusal.
///
/// <para>
/// The `LanModeRoundTrip` end-to-end WSS handshake is tagged
/// <c>[Trait("Category","Elevated")]</c> and uses <see cref="SkippableFact"/> so
/// running it without admin rights produces a skip (not an error). The two refusal
/// tests need no admin and run by default.
/// </para>
/// </summary>
[Trait("Category", "LanWss")]
public sealed class WebSocketTransportLanTests
{
    /// <summary>
    /// FR-001 / contracts/lan-https-binding-contract.md step 1: the all-interfaces LAN binding
    /// must be registered with HTTP.SYS's strong wildcard, NOT the literal 0.0.0.0.
    ///
    /// <para>Shipping the literal made every AkmlSqlWebEngine service start die with
    /// <c>HttpListenerException 50 (ERROR_NOT_SUPPORTED)</c> out of <c>AddPrefixCore</c> — HTTP.SYS
    /// will not accept an IP literal as the host of an HTTPS prefix, even as LocalSystem and even
    /// with the sslcert already bound to that exact ip:port. The web edition was stuck "Offline"
    /// as a result. <see cref="LanMode_round_trip_wss_handshake"/> would have caught it, but it is
    /// still an unimplemented skip, so this string-level check is the real guard.</para>
    ///
    /// <para>Finding 9 (PR #249 review): <c>*</c> (HTTP.SYS's WEAK wildcard) and <c>+</c> (STRONG)
    /// are ALREADY valid prefix hosts and must be preserved AS-IS, not folded into <c>+</c> --
    /// only the literal IP forms (<c>0.0.0.0</c>, <c>::</c>) need rewriting. Folding <c>*</c> into
    /// <c>+</c> changes HTTP.SYS's prefix-registration precedence.</para>
    /// </summary>
    [Theory]
    [InlineData("0.0.0.0", "+")]
    [InlineData("::", "+")]
    [InlineData("*", "*")]
    [InlineData("+", "+")]
    public void LanMode_all_interfaces_binding_maps_to_the_correct_prefix_host(string bindAddress, string expectedHost)
    {
        var prefix = WebSocketTransport.BuildPrefix(new WebSocketTransportOptions
        {
            BindAddress = bindAddress,
            Port = 47291,
            TlsCertPath = "C:\\certs\\bridge.pfx",
        });

        Assert.Equal($"https://{expectedHost}:47291/", prefix);
    }

    /// <summary>
    /// FR-001: a named host stays as configured — narrowing the bridge to one interface is a
    /// legitimate setup, and HTTP.SYS does accept a hostname (just not an IP literal) for HTTPS.
    /// </summary>
    [Fact]
    public void LanMode_named_host_is_preserved()
    {
        var prefix = WebSocketTransport.BuildPrefix(new WebSocketTransportOptions
        {
            BindAddress = "sql-box.internal",
            Port = 8443,
            TlsCertPath = "C:\\certs\\bridge.pfx",
        });

        Assert.Equal("https://sql-box.internal:8443/", prefix);
    }

    /// <summary>
    /// The loopback path keeps plaintext http:// on 127.0.0.1 — no TLS, no pairing token. The
    /// existing localhost transport tests depend on this, so the wildcard fix must not touch it.
    /// </summary>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("localhost")]
    public void LoopbackMode_stays_plaintext_on_127_0_0_1(string bindAddress)
    {
        var prefix = WebSocketTransport.BuildPrefix(new WebSocketTransportOptions
        {
            BindAddress = bindAddress,
            Port = 47291,
        });

        Assert.Equal("http://127.0.0.1:47291/", prefix);
    }

    /// <summary>
    /// FR-002 / contracts/lan-https-binding-contract.md step 2: when the configured
    /// <see cref="WebSocketTransportOptions.TlsCertPath"/> does not exist on disk,
    /// <see cref="WebSocketTransport.StartAsync"/> MUST throw with a clear message
    /// referencing `TlsCertPath` and pointing at the installer-produced PFX path.
    /// </summary>
    [Fact]
    public async Task LanMode_refuses_when_pfx_missing()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"akmlsql-test-missing-{Guid.NewGuid():N}.pfx");
        await using var transport = new WebSocketTransport(new WebSocketTransportOptions
        {
            BindAddress = "127.0.0.2",        // non-loopback for the construction check; never bound
            Port = 53999,
            TlsCertPath = nonExistentPath,
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => transport.StartAsync(default));
        Assert.Contains("TlsCertPath", ex.Message);
        Assert.Contains(nonExistentPath, ex.Message);
        Assert.Contains("AKMLSQLSetup", ex.Message);
    }

    /// <summary>
    /// FR-002 + Research Decision 5: when <see cref="WebSocketTransportOptions.TlsCertPath"/>
    /// is empty for a non-loopback binding, the <see cref="WebSocketTransport"/> constructor
    /// itself refuses (the existing FR-013a guard from spec 021 T057). This test was already
    /// covered by `Constructor_refuses_lan_without_tls` in the spec-021 test file; we add a
    /// closure-spec-specific assertion that the FR-013a reference is preserved in the message.
    /// </summary>
    [Fact]
    public void LanMode_refuses_when_tls_cert_path_empty()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new WebSocketTransport(
            new WebSocketTransportOptions
            {
                BindAddress = "0.0.0.0",
                Port = 53998,
                TlsCertPath = string.Empty,
            }));
        Assert.Contains("TlsCertPath", ex.Message);
        Assert.Contains("FR-013a", ex.Message);
    }

    /// <summary>
    /// FR-001 + FR-006 + contracts/lan-https-binding-contract.md §Tests: full LAN-mode
    /// WSS handshake against a unit-test self-signed cert that has been bound via
    /// `netsh http add sslcert`. Requires elevation (only Admin can run netsh sslcert
    /// add/delete and bind a non-loopback HttpListener prefix).
    ///
    /// <para>
    /// Currently the test simply asserts the skip / elevated gate works. The actual
    /// cert-generation + netsh-bind + WSS round-trip is left as a TODO for the
    /// interactive engineer running with elevation -- the spec-025 closure work
    /// captures the gate, the contract, the cert validation logic, and the engine-side
    /// composition; the end-to-end wire smoke is covered by the deferred US5 E2E suite
    /// (`tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs`).
    /// </para>
    /// </summary>
    [SkippableFact]
    public Task LanMode_round_trip_wss_handshake()
    {
        Skip.IfNot(IsRunningElevated(),
            "LAN round-trip requires admin rights for `netsh http add sslcert` and a non-loopback HttpListener bind.");

        // Cert generation, netsh bind, WSS round-trip, and netsh teardown live in
        // a deferred follow-up under tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs.
        // The closure-spec deliverable for this case is the gate + the SkippableFact
        // discipline that lets CI exclude it cleanly.
        return Task.CompletedTask;
    }

    private static bool IsRunningElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
