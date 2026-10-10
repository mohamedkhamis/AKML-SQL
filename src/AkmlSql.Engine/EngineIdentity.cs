using System;
using System.Security.Principal;

namespace AkmlSql.Engine;

/// <summary>
/// Spec 041 (research R29, N8, FR-063) — how this engine runs, told to the web in the handshake: <c>ide</c> when
/// SSMS started it over the named pipe (it runs as the signed-in user and shares their styles folder),
/// <c>service</c> for the Windows service, <c>console</c> for an interactive <c>--web</c>. The web names the engine's
/// styles "Shared with SSMS" only for <c>ide</c>. Set once at start-up by <see cref="EngineHost"/>.
/// </summary>
public static class EngineIdentity
{
    public const string Ide = "ide";
    public const string Service = "service";
    public const string Console = "console";

    /// <summary><see cref="Ide"/>, <see cref="Service"/> or <see cref="Console"/>; null until the host sets it.</summary>
    public static string? HostKind { get; internal set; }

    /// <summary>The account the engine runs as (<c>DOMAIN\user</c>); shown on hover and in Diagnostics.</summary>
    public static string? RunsAs { get; internal set; }

    /// <summary>Record how the engine was started, and as whom.</summary>
    internal static void Set(string hostKind)
    {
        HostKind = hostKind;
        try { RunsAs = WindowsIdentity.GetCurrent().Name; }
        catch (Exception) { RunsAs = Environment.UserName; }   // not Windows, or no access token
    }
}
