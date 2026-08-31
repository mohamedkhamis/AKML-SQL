using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// The scheme decision that used to be <c>IsLocalhost ? "ws://" : "wss://"</c>.
///
/// <para>
/// That one line was the cause of a dead end: the engine ships either bound to loopback without
/// TLS, or bound to the LAN with TLS required, and a checkbox in the connection form cannot know
/// which the installer chose. Ticking "Localhost" against a TLS bridge dialled plaintext, the TLS
/// listener reset it, and the UI sat on "Connecting…" with nothing to explain why.
/// </para>
/// </summary>
public sealed class EngineEndpointTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void Loopback_hosts_are_recognised(string host) =>
        Assert.True(EngineEndpoint.IsLoopbackHost(host));

    [Theory]
    [InlineData("my-machine")]
    [InlineData("192.168.1.10")]
    [InlineData("engine.example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Non_loopback_hosts_are_not_mistaken_for_loopback(string? host) =>
        Assert.False(EngineEndpoint.IsLoopbackHost(host));

    [Fact]
    public void A_loopback_host_tries_plaintext_first_then_tls()
    {
        var urls = EngineEndpoint.CandidateUrls("127.0.0.1", 47291);

        Assert.Equal(
            ["ws://127.0.0.1:47291/akmlsql", "wss://127.0.0.1:47291/akmlsql"],
            urls);
    }

    [Fact]
    public void A_named_host_is_tls_only()
    {
        var urls = EngineEndpoint.CandidateUrls("my-machine", 47291);

        Assert.Equal(["wss://my-machine:47291/akmlsql"], urls);
    }

    [Fact]
    public void A_named_host_is_never_downgraded_even_if_plaintext_was_remembered()
    {
        // A saved connection could carry a stale "ws" from when it pointed at loopback. Replaying
        // that against a network host would be a silent downgrade, which is worse than failing.
        var urls = EngineEndpoint.CandidateUrls("my-machine", 47291, rememberedScheme: "ws");

        Assert.Equal(["wss://my-machine:47291/akmlsql"], urls);
        Assert.DoesNotContain(urls, u => u.StartsWith("ws://"));
    }

    [Fact]
    public void A_remembered_scheme_is_tried_first_so_a_known_good_connection_needs_one_attempt()
    {
        var urls = EngineEndpoint.CandidateUrls("127.0.0.1", 47291, rememberedScheme: "wss");

        Assert.Equal("wss://127.0.0.1:47291/akmlsql", urls[0]);
        Assert.Contains("ws://127.0.0.1:47291/akmlsql", urls);   // fallback still available
    }

    [Fact]
    public void A_remembered_scheme_does_not_produce_a_duplicate_candidate()
    {
        var urls = EngineEndpoint.CandidateUrls("127.0.0.1", 47291, rememberedScheme: "ws");

        Assert.Equal(urls.Count, urls.Distinct().Count());
        Assert.Equal("ws://127.0.0.1:47291/akmlsql", urls[0]);
    }

    [Fact]
    public void An_ipv6_literal_is_bracketed_so_the_url_is_valid()
    {
        var urls = EngineEndpoint.CandidateUrls("::1", 47291);

        Assert.All(urls, u => Assert.Contains("[::1]:47291", u));
    }

    [Fact]
    public void A_custom_port_is_honoured()
    {
        var urls = EngineEndpoint.CandidateUrls("localhost", 5099);

        Assert.All(urls, u => Assert.Contains(":5099/akmlsql", u));
    }

    [Theory]
    [InlineData("ws://127.0.0.1:47291/akmlsql", "ws")]
    [InlineData("wss://host:47291/akmlsql", "wss")]
    public void SchemeOf_reads_the_scheme_back(string url, string expected) =>
        Assert.Equal(expected, EngineEndpoint.SchemeOf(url));

    [Fact]
    public void A_blank_host_is_rejected_rather_than_producing_a_nonsense_url() =>
        Assert.Throws<ArgumentException>(() => EngineEndpoint.CandidateUrls(" ", 47291));
}
