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
}
