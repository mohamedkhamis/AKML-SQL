using System;
using AkmlSql.Web.Services;
using Microsoft.JSInterop;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Adding a remote engine: reading the address a person pasted, and explaining a failed connect.
///
/// <para>
/// The report that prompted this: pairing with <c>wss://162.220.54.191:47291/akmlsql</c> showed
/// only "WebSocket connect failed … at ws.onerror (akml-bridge.js:56:28)". Three separate things
/// were wrong -- the engine listened on another port, the page's own policy blocked remote engines,
/// and the certificate was untrusted -- and the message could not tell any of them apart.
/// </para>
/// </summary>
public sealed class EngineConnectDiagnosisTests
{
    // --- reading the address ----------------------------------------------------------------

    [Theory]
    [InlineData("162.220.54.191", 47291, "162.220.54.191", 47291)]
    [InlineData("162.220.54.191:59417", 47291, "162.220.54.191", 59417)]
    [InlineData(" engine-host:59417 ", 47291, "engine-host", 59417)]
    [InlineData("wss://162.220.54.191:59417/akmlsql", 47291, "162.220.54.191", 59417)]
    [InlineData("https://engine.example.com:59417/", 47291, "engine.example.com", 59417)]
    [InlineData("wss://engine.example.com/akmlsql", 59417, "engine.example.com", 59417)]
    [InlineData("[fe80::1]:59417", 47291, "fe80::1", 59417)]
    [InlineData("fe80::1", 47291, "fe80::1", 47291)]
    [InlineData("localhost", 47291, "localhost", 47291)]
    public void APastedAddress_FillsHostAndPort(string input, int fallback, string host, int port)
    {
        Assert.True(EngineEndpoint.TryParseAddress(input, fallback, out var parsedHost, out var parsedPort));
        Assert.Equal(host, parsedHost);
        Assert.Equal(port, parsedPort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("host:notaport")]
    [InlineData("host:70000")]
    [InlineData("host name with spaces")]
    [InlineData("[fe80::1")]
    public void NonsenseIsRejected(string? input)
    {
        Assert.False(EngineEndpoint.TryParseAddress(input, 47291, out _, out _));
    }

    // --- explaining a failure ---------------------------------------------------------------

    [Fact]
    public void ThePagesOwnPolicy_IsNamed_AndTheFixIsAnUpdate()
    {
        var error = new JSException(
            "The connection to wss://162.220.54.191:47291/akmlsql was blocked by this page's Content-Security-Policy (connect-src), " +
            "so it never left this computer.\nError: …\n    at ws.onerror (http://localhost/js/akml-bridge.js:56:28)");

        var d = EngineConnectDiagnosis.For("162.220.54.191", 47291, error);

        Assert.Equal(EngineConnectProblem.BlockedByPagePolicy, d.Problem);
        Assert.Contains("only allowed to connect to engines on this computer", d.Summary, StringComparison.Ordinal);
        Assert.Contains(d.Steps, s => s.Contains("Repair AKML SQL Web hosting", StringComparison.Ordinal));
        Assert.Null(d.TrustUrl); // accepting a certificate would not help
    }

    [Fact]
    public void ARemoteEngine_ThatDidNotAnswer_GetsTheTrustLinkFirst_AndThePortAndPinSteps()
    {
        var error = new JSException(
            "WebSocket connect failed (wss://162.220.54.191:47291/akmlsql).\nError: WebSocket connect failed\n    at ws.onerror (http://localhost/js/akml-bridge.js:56:28)");

        var d = EngineConnectDiagnosis.For("162.220.54.191", 47291, error);

        Assert.Equal(EngineConnectProblem.NotReachable, d.Problem);
        Assert.Equal("https://162.220.54.191:47291/akmlsql", d.TrustUrl);
        Assert.Contains("162.220.54.191:47291", d.Steps[0], StringComparison.Ordinal);
        Assert.Contains(d.Steps, s => s.Contains("Bridge port", StringComparison.Ordinal));
        Assert.Contains(d.Steps, s => s.Contains("pairing-pin.txt", StringComparison.Ordinal));
        Assert.Contains(d.Steps, s => s.Contains("firewall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheStackTrace_IsKeptOutOfTheMessage()
    {
        var error = new JSException("WebSocket connect failed (wss://h:1/akmlsql).\nError: x\n    at ws.onerror (http://localhost/js/akml-bridge.js:56:28)");

        var d = EngineConnectDiagnosis.For("h", 1, error);

        Assert.Equal("WebSocket connect failed (wss://h:1/akmlsql).", d.RawError);
    }

    [Fact]
    public void ALocalEngine_ThatDidNotAnswer_PointsAtTheService_NotAtCertificates()
    {
        var d = EngineConnectDiagnosis.For("localhost", 47291, new JSException("WebSocket connect failed (ws://localhost:47291/akmlsql)."));

        Assert.Null(d.TrustUrl);
        Assert.Contains(d.Steps, s => s.Contains("AkmlSqlWebEngine", StringComparison.Ordinal));
    }

    [Fact]
    public void AnIpv6Engine_GetsAValidTrustUrl()
    {
        var d = EngineConnectDiagnosis.For("fe80::1", 59417, new JSException("WebSocket connect failed."));

        Assert.Equal("https://[fe80::1]:59417/akmlsql", d.TrustUrl);
    }
}
