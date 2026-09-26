using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Web.Services;

/// <summary>
/// Works out which URL to dial for an engine connection.
///
/// <para>
/// This used to be one line — <c>IsLocalhost ? "ws://" : "wss://"</c> — and that single decision was
/// the cause of a dead end users hit constantly. The engine ships in two shapes: bound to loopback
/// with no TLS, or bound to the LAN with TLS required. The browser cannot know which one the
/// installer chose, but the checkbox in the connection form quietly claimed it did. Tick "Localhost"
/// against a TLS bridge and the plaintext upgrade is reset by the TLS listener, so the pill sits at
/// "Connecting…" forever with nothing to explain why.
/// </para>
///
/// <para>
/// So the scheme is discovered rather than assumed: try the likely candidates in order and remember
/// which one answered. The one rule that is never bent is that a non-loopback host is
/// <b>wss-only</b> — a silent downgrade to plaintext on the network is worse than a failure,
/// because it looks like it worked.
/// </para>
/// </summary>
public static class EngineEndpoint
{
    /// <summary>The bridge's default port (<c>WebSocketTransportOptions.Port</c>).</summary>
    public const int DefaultPort = 47291;

    /// <summary>Path the bridge listens on.</summary>
    public const string Path = "/akmlsql";

    /// <summary>True when <paramref name="host"/> is a literal loopback address or name.</summary>
    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var h = host.Trim().Trim('[', ']');
        return h.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || h.Equals("127.0.0.1", StringComparison.Ordinal)
            || h.Equals("::1", StringComparison.Ordinal);
    }

    /// <summary>
    /// The URLs to try, best first.
    ///
    /// <list type="bullet">
    ///   <item><description>
    ///     A remembered scheme always goes first, so a connection that has worked before reconnects
    ///     in one attempt rather than re-probing on every startup.
    ///   </description></item>
    ///   <item><description>
    ///     A loopback host may try plaintext, because that is the documented localhost mode and
    ///     there is no network to eavesdrop on. Both schemes are offered so the same saved
    ///     connection keeps working if the engine is later reconfigured.
    ///   </description></item>
    ///   <item><description>
    ///     Any other host is <c>wss://</c> only.
    ///   </description></item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> CandidateUrls(string host, int port, string? rememberedScheme = null)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host is required.", nameof(host));

        var schemes = new List<string>();

        if (!string.IsNullOrWhiteSpace(rememberedScheme))
        {
            schemes.Add(rememberedScheme!.Trim().ToLowerInvariant());
        }

        if (IsLoopbackHost(host))
        {
            // Plaintext first: a loopback bridge is the no-TLS, no-PIN mode, and it is both the
            // commoner install and the cheaper handshake.
            schemes.Add("ws");
            schemes.Add("wss");
        }
        else
        {
            schemes.Add("wss");
        }

        return schemes
            .Distinct(StringComparer.Ordinal)
            .Where(s => s is "ws" or "wss")
            // Never dial plaintext at a host that is not loopback, even if that scheme was
            // remembered from an earlier, differently-configured connection.
            .Where(s => s == "wss" || IsLoopbackHost(host))
            .Select(s => $"{s}://{FormatHost(host)}:{port}{Path}")
            .ToList();
    }

    /// <summary>
    /// Reads what a person typed or pasted as an engine address: <c>host</c>, <c>host:port</c>,
    /// <c>[ipv6]:port</c>, a bare IPv6 literal, or a whole URL such as
    /// <c>wss://host:59417/akmlsql</c> or <c>https://host:59417/</c> (the address the trust page
    /// is opened at). A port in the text wins over <paramref name="fallbackPort"/>.
    /// </summary>
    public static bool TryParseAddress(string? input, int fallbackPort, out string host, out int port)
    {
        host = string.Empty;
        port = fallbackPort;
        var text = input?.Trim() ?? string.Empty;
        if (text.Length == 0) return false;

        if (text.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return false;
            host = uri.Host.Trim('[', ']');
            if (!uri.IsDefaultPort) port = uri.Port;
            return IsValidHost(host);
        }

        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0) return false;
            host = text[1..close];
            var rest = text[(close + 1)..];
            if (rest.Length > 0 && !TryReadPort(rest, ref port)) return false;
            return IsValidHost(host);
        }

        var firstColon = text.IndexOf(':');
        if (firstColon >= 0 && firstColon == text.LastIndexOf(':'))
        {
            // One colon: host:port. More than one is a bare IPv6 literal.
            if (!TryReadPort(text[firstColon..], ref port)) return false;
            text = text[..firstColon];
        }

        host = text.TrimEnd('/');
        return IsValidHost(host);
    }

    private static bool TryReadPort(string colonAndPort, ref int port)
    {
        if (colonAndPort.Length < 2 || colonAndPort[0] != ':') return false;
        if (!int.TryParse(colonAndPort[1..].TrimEnd('/'), out var value) || value is < 1 or > 65535) return false;
        port = value;
        return true;
    }

    private static bool IsValidHost(string host) =>
        host.Length > 0 && Uri.CheckHostName(host) != UriHostNameType.Unknown;

    /// <summary>The scheme part of a URL produced by <see cref="CandidateUrls"/>.</summary>
    public static string SchemeOf(string url)
    {
        var i = url.IndexOf("://", StringComparison.Ordinal);
        return i < 0 ? string.Empty : url[..i];
    }

    /// <summary>Bracket a bare IPv6 literal so it is a valid URL authority.</summary>
    private static string FormatHost(string host)
    {
        var h = host.Trim();
        if (h.Contains(':') && !h.StartsWith('[')) return $"[{h}]";
        return h;
    }
}
