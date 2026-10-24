using System.Text.Json;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Facts about the AkmlSqlWebEngine service installed on this machine, read from where the
/// installer writes them rather than assumed.
/// <para>
/// The bridge port in particular: tests used to hard-code the default 47291, but the installer
/// picks another port when that one is taken (on the reference machine it is 59417), so the tests
/// dialled a port nothing listened on -- the same dead end a user pairing by hand hit.
/// </para>
/// </summary>
internal static class InstalledEngine
{
    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AKML SQL Web");

    /// <summary>The bridge's default port, used when no config has been written.</summary>
    public const int DefaultBridgePort = 47291;

    /// <summary><c>bridge.port</c> from <c>%ProgramData%\AKML SQL Web\config.json</c>.</summary>
    public static int BridgePort
    {
        get
        {
            var path = Path.Combine(StateDirectory, "config.json");
            if (!File.Exists(path)) return DefaultBridgePort;

            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.TryGetProperty("bridge", out var bridge)
                   && bridge.TryGetProperty("port", out var port)
                   && port.TryGetInt32(out var value)
                ? value
                : DefaultBridgePort;
        }
    }

    /// <summary>True when the installed engine's config says it listens on the network (TLS + pairing), not loopback.</summary>
    public static bool IsNetworkMode
    {
        get
        {
            var path = Path.Combine(StateDirectory, "config.json");
            if (!File.Exists(path)) return false;
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var bind = json.RootElement.TryGetProperty("bridge", out var bridge)
                       && bridge.TryGetProperty("bindAddress", out var b) ? b.GetString() : null;
            return bind is not null and not "127.0.0.1" and not "localhost" and not "::1";
        }
    }

    /// <summary>The current pairing PIN (single use: the engine writes a new one after each pairing).</summary>
    public static string? PairingPin
    {
        get
        {
            var path = Path.Combine(StateDirectory, "pairing-pin.txt");
            var pin = File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
            return pin.Length == 0 ? null : pin;
        }
    }

    /// <summary>The installed site's address on this computer (the port of INSTALL-SUMMARY.txt's URL line), or null.</summary>
    public static string? SiteUrl
    {
        get
        {
            var path = Path.Combine(StateDirectory, "INSTALL-SUMMARY.txt");
            if (!File.Exists(path)) return null;
            var lines = File.ReadLines(path).ToList();
            // A LAN install with HTTPS lists https://{computer}:{https port}/ first and the plain local site on the next line
            // ("(on this computer also http://localhost:{port}/)"); plain HTTP to the HTTPS port is reset, so that one is used.
            var local = lines.Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("(on this computer also ", StringComparison.Ordinal));
            if (local is not null && Uri.TryCreate(local["(on this computer also ".Length..].TrimEnd(')', ' '), UriKind.Absolute, out var plain))
                return $"http://localhost:{plain.Port}/";
            var line = lines.FirstOrDefault(l => l.StartsWith("URL:", StringComparison.Ordinal));
            return line is not null && Uri.TryCreate(line["URL:".Length..].Trim(), UriKind.Absolute, out var uri)
                ? $"http://localhost:{uri.Port}/"
                : null;
        }
    }
}
